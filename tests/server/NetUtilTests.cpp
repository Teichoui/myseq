// Tests for server/NetUtil.h over real loopback TCP sockets.
// Build and run with tests\server\run-tests.ps1.

#define NOMINMAX
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <cstdio>
#include <thread>
#include <vector>
#include <chrono>
#include <algorithm>

#include "../../server/NetUtil.h"

#pragma comment(lib, "ws2_32.lib")

static int failures = 0;

#define CHECK(cond, msg) do { \
	if (cond) printf("  ok    %s\n", msg); \
	else { printf("  FAIL  %s\n", msg); ++failures; } \
} while (0)

struct Pair { SOCKET server; SOCKET client; };

// A connected loopback pair: 'server' is the accepted end, 'client' the connecting end.
static Pair makePair()
{
	SOCKET listener = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	sockaddr_in addr = {};
	addr.sin_family = AF_INET;
	addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
	addr.sin_port = 0;
	bind(listener, (sockaddr*)&addr, sizeof(addr));
	listen(listener, 1);
	int len = sizeof(addr);
	getsockname(listener, (sockaddr*)&addr, &len);

	SOCKET client = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
	connect(client, (sockaddr*)&addr, sizeof(addr));
	SOCKET server = accept(listener, NULL, NULL);
	closesocket(listener);
	return { server, client };
}

static void setNonBlocking(SOCKET s)
{
	u_long on = 1;
	ioctlsocket(s, FIONBIO, &on);
}

static void setSendBuffer(SOCKET s, int bytes)
{
	setsockopt(s, SOL_SOCKET, SO_SNDBUF, (const char*)&bytes, sizeof(bytes));
}

static void setRecvBuffer(SOCKET s, int bytes)
{
	setsockopt(s, SOL_SOCKET, SO_RCVBUF, (const char*)&bytes, sizeof(bytes));
}

static std::vector<char> makePayload(size_t n)
{
	std::vector<char> v(n);
	for (size_t i = 0; i < n; ++i)
		v[i] = (char)((i * 31 + 7) & 0xFF);
	return v;
}

// Reads exactly n bytes slowly, so the sender's buffers fill up.
static std::vector<char> slowRead(SOCKET s, size_t n)
{
	std::vector<char> out;
	out.reserve(n);
	char buf[4096];
	while (out.size() < n)
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(2));
		int got = recv(s, buf, sizeof(buf), 0);
		if (got <= 0) break;
		out.insert(out.end(), buf, buf + got);
	}
	return out;
}

static void testSendAllLargeOnNonBlocking()
{
	printf("sendAll: large payload on a non-blocking socket with a slow reader\n");

	Pair p = makePair();
	setNonBlocking(p.server);
	setSendBuffer(p.server, 4096);
	setRecvBuffer(p.client, 4096);

	const size_t N = 2 * 1024 * 1024;
	std::vector<char> payload = makePayload(N);

	// Control: with a client that is not reading, plain send() eventually fails with
	// WSAEWOULDBLOCK. The old code ignored that result and threw the reply away.
	size_t prefilled = 0;
	bool wouldBlock = false;
	while (prefilled < 256u * 1024 * 1024)
	{
		int n = send(p.server, payload.data(), 8192, 0);
		if (n == SOCKET_ERROR)
		{
			wouldBlock = (WSAGetLastError() == WSAEWOULDBLOCK);
			break;
		}
		prefilled += n;
	}
	CHECK(wouldBlock, "control: once the buffers are full a plain send() fails with WSAEWOULDBLOCK");

	std::vector<char> received;
	std::thread reader([&] {
		// Drain what the control stuffed into the pipe, then read the real payload slowly.
		std::vector<char> sink(65536);
		size_t drained = 0;
		while (drained < prefilled)
		{
			int got = recv(p.client, sink.data(), (int)std::min(sink.size(), prefilled - drained), 0);
			if (got <= 0) break;
			drained += got;
		}
		received = slowRead(p.client, N);
	});

	bool ok = sendAll(p.server, payload.data(), N);
	reader.join();

	CHECK(ok, "sendAll reports success");
	CHECK(received.size() == N, "reader received every byte");
	CHECK(received == payload, "received bytes match what was sent");

	closesocket(p.server);
	closesocket(p.client);
}

static void testSendAllFailsOnClosedPeer()
{
	printf("sendAll: peer goes away\n");

	Pair p = makePair();
	setNonBlocking(p.server);
	closesocket(p.client);

	std::vector<char> payload = makePayload(8 * 1024 * 1024);
	bool ok = sendAll(p.server, payload.data(), payload.size(), 500);
	CHECK(!ok, "sendAll returns false instead of reporting success");

	closesocket(p.server);
}

static void testRecvAllFragmented()
{
	printf("recvAll: 4-byte request arriving in pieces\n");

	const char msg[4] = { 0x11, 0x22, 0x33, 0x44 };

	// Control: the old code did one recv() and dropped the client unless it got all 4 bytes.
	{
		Pair c = makePair();
		setNonBlocking(c.server);
		send(c.client, msg, 1, 0);
		waitForSocket(c.server, false, 2000);
		char oneShot[4] = {};
		int got = recv(c.server, oneShot, sizeof(oneShot), 0);
		CHECK(got >= 1 && got < 4, "control: a single recv() returns a short read");
		closesocket(c.server);
		closesocket(c.client);
	}

	Pair p = makePair();
	setNonBlocking(p.server);
	setsockopt(p.client, IPPROTO_TCP, TCP_NODELAY, "\1", 1);

	std::thread writer([&] {
		send(p.client, msg, 1, 0);
		std::this_thread::sleep_for(std::chrono::milliseconds(60));
		send(p.client, msg + 1, 1, 0);
		std::this_thread::sleep_for(std::chrono::milliseconds(60));
		send(p.client, msg + 2, 2, 0);
	});

	// Wait until the first byte is readable, as FD_READ would signal in the server.
	waitForSocket(p.server, false, 2000);

	char got[4] = {};
	bool ok = recvAll(p.server, got, 4);
	writer.join();

	CHECK(ok, "recvAll waits for the remaining bytes");
	CHECK(memcmp(got, msg, 4) == 0, "assembled request matches the bytes sent");

	closesocket(p.server);
	closesocket(p.client);
}

static void testRecvAllWholeAtOnce()
{
	printf("recvAll: complete request\n");

	Pair p = makePair();
	setNonBlocking(p.server);
	const char msg[4] = { 1, 2, 3, 4 };
	send(p.client, msg, 4, 0);
	waitForSocket(p.server, false, 2000);

	char got[4] = {};
	CHECK(recvAll(p.server, got, 4), "returns true");
	CHECK(memcmp(got, msg, 4) == 0, "bytes match");

	closesocket(p.server);
	closesocket(p.client);
}

static void testRecvAllNothingReadable()
{
	printf("recvAll: nothing to read\n");

	Pair p = makePair();
	setNonBlocking(p.server);

	char got[4] = {};
	auto t0 = std::chrono::steady_clock::now();
	bool ok = recvAll(p.server, got, 4, 1000);
	auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();

	CHECK(!ok, "returns false");
	CHECK(ms < 200, "returns at once instead of waiting out the timeout");

	closesocket(p.server);
	closesocket(p.client);
}

static void testRecvAllPeerClosed()
{
	printf("recvAll: peer closes mid-request\n");

	Pair p = makePair();
	setNonBlocking(p.server);
	send(p.client, "ab", 2, 0);
	closesocket(p.client);
	waitForSocket(p.server, false, 2000);

	char got[4] = {};
	CHECK(!recvAll(p.server, got, 4, 500), "returns false");

	closesocket(p.server);
}

static void testRecvAllStalledPartial()
{
	printf("recvAll: sender stalls after part of the request\n");

	Pair p = makePair();
	setNonBlocking(p.server);
	send(p.client, "ab", 2, 0);
	waitForSocket(p.server, false, 2000);

	char got[4] = {};
	auto t0 = std::chrono::steady_clock::now();
	bool ok = recvAll(p.server, got, 4, 300);
	auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now() - t0).count();

	CHECK(!ok, "gives up with false");
	CHECK(ms >= 250 && ms < 1500, "after roughly the timeout");

	closesocket(p.server);
	closesocket(p.client);
}

static void testWalkGuard()
{
	printf("WalkGuard: linked-list walks\n");

	// A -> B -> C -> A. The unguarded loop never ends; the guarded one visits each node once.
	const std::uint64_t next[4] = { 0, 2, 3, 1 };	// next[1]=2, next[2]=3, next[3]=1
	{
		WalkGuard g;
		int visits = 0;
		std::uint64_t p = 1;
		while (p && g.visit(p) && visits < 1000)
		{
			++visits;
			p = next[p];
		}
		CHECK(visits == 3, "cycle of three nodes is walked exactly once");
	}

	{
		WalkGuard g;
		int visits = 0;
		std::uint64_t p = 7;
		while (p && g.visit(p) && visits < 1000)
		{
			++visits;
			p = 7;	// self link
		}
		CHECK(visits == 1, "self-linked node is visited once");
	}

	{
		WalkGuard g;
		int visits = 0;
		for (std::uint64_t p = 1; p <= 50 && g.visit(p); ++p)
			++visits;
		CHECK(visits == 50, "a long acyclic list is fully walked");
	}

	{
		WalkGuard g(10);
		int visits = 0;
		for (std::uint64_t p = 1; p <= 1000 && g.visit(p); ++p)
			++visits;
		CHECK(visits == 10, "node limit stops a walk that never repeats");
	}
}

int main()
{
	WSADATA wsa;
	WSAStartup(MAKEWORD(2, 2), &wsa);

	testSendAllLargeOnNonBlocking();
	testSendAllFailsOnClosedPeer();
	testRecvAllFragmented();
	testRecvAllWholeAtOnce();
	testRecvAllNothingReadable();
	testRecvAllPeerClosed();
	testRecvAllStalledPartial();
	testWalkGuard();

	WSACleanup();
	printf("\n%s (%d failure%s)\n", failures ? "FAILED" : "PASSED", failures, failures == 1 ? "" : "s");
	return failures ? 1 : 0;
}
