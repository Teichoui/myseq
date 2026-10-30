// NetUtil.h : small socket and pointer-walk helpers used by NetworkServer.
// Header only so they can be tested without the rest of the server.

#pragma once

#include <winsock2.h>
#include <cstddef>
#include <cstdint>
#include <chrono>
#include <unordered_set>

// Wait until the socket is writable (forWrite) or readable. False on timeout or error.
inline bool waitForSocket(SOCKET s, bool forWrite, int timeoutMs)
{
	fd_set fds;
	FD_ZERO(&fds);
	FD_SET(s, &fds);

	timeval tv;
	tv.tv_sec = timeoutMs / 1000;
	tv.tv_usec = (timeoutMs % 1000) * 1000;

	int rc = forWrite ? select(0, NULL, &fds, NULL, &tv) : select(0, &fds, NULL, NULL, &tv);
	return rc > 0;
}

// Milliseconds left until deadline, or 0 once it has passed.
inline int msUntil(std::chrono::steady_clock::time_point deadline)
{
	auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - std::chrono::steady_clock::now()).count();
	return left > 0 ? (int)left : 0;
}

// Send all of buf. The accepted socket can be non-blocking (WSAAsyncSelect), where send()
// may take only part of the data or fail with WSAEWOULDBLOCK, so loop until it is all out.
// timeoutMs is the most time the whole call may spend waiting, not the limit for each wait,
// so a client that drains the socket a little at a time cannot hold the caller up for longer.
inline bool sendAll(SOCKET s, const char* buf, std::size_t len, int timeoutMs = 5000)
{
	auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
	std::size_t sent = 0;

	while (sent < len)
	{
		std::size_t remaining = len - sent;
		int chunk = remaining > 0x10000 ? 0x10000 : (int)remaining;
		int n = send(s, buf + sent, chunk, 0);

		if (n == SOCKET_ERROR)
		{
			if (WSAGetLastError() != WSAEWOULDBLOCK || !waitForSocket(s, true, msUntil(deadline)))
				return false;
			continue;
		}
		sent += n;
	}
	return true;
}

enum class RecvResult
{
	Complete,	// all len bytes were read
	NoData,		// nothing was readable yet; the connection is still fine
	Closed		// the peer closed the connection, an error occurred, or the request stalled part way
};

// Read exactly len bytes. A stream socket may deliver a small message in pieces, so once
// the first bytes have arrived wait for the rest.
//
// With WSAAsyncSelect, reading again after the first recv() can leave an FD_READ message queued
// for data that has already been consumed. Handling it finds nothing to read, which is not a
// disconnect, so that case is reported as NoData and returns at once.
inline RecvResult recvAll(SOCKET s, char* buf, std::size_t len, int timeoutMs = 1000)
{
	// timeoutMs is the most time the whole call may spend waiting for the rest of the request.
	auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
	std::size_t got = 0;

	while (got < len)
	{
		int n = recv(s, buf + got, (int)(len - got), 0);

		if (n == 0)
			return RecvResult::Closed;

		if (n == SOCKET_ERROR)
		{
			if (WSAGetLastError() != WSAEWOULDBLOCK)
				return RecvResult::Closed;
			if (got == 0)
				return RecvResult::NoData;
			if (!waitForSocket(s, false, msUntil(deadline)))
				return RecvResult::Closed;
			continue;
		}
		got += n;

		if (got < len && !waitForSocket(s, false, msUntil(deadline)))
			return RecvResult::Closed;
	}
	return RecvResult::Complete;
}

// Stops a linked-list walk through game memory from looping forever on a cycle.
class WalkGuard
{
public:
	explicit WalkGuard(std::size_t maxNodes = 100000) : maxNodes_(maxNodes) {}

	// True the first time p is seen; false for a repeat or once maxNodes were visited.
	bool visit(std::uint64_t p)
	{
		if (seen_.size() >= maxNodes_)
			return false;
		return seen_.insert(p).second;
	}

private:
	std::size_t maxNodes_;
	std::unordered_set<std::uint64_t> seen_;
};
