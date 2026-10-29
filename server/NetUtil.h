// NetUtil.h : small socket and pointer-walk helpers used by NetworkServer.
// Header only so they can be tested without the rest of the server.

#pragma once

#include <winsock2.h>
#include <cstddef>
#include <cstdint>
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

// Send all of buf. The accepted socket can be non-blocking (WSAAsyncSelect), where send()
// may take only part of the data or fail with WSAEWOULDBLOCK, so loop until it is all out.
inline bool sendAll(SOCKET s, const char* buf, std::size_t len, int timeoutMs = 5000)
{
	std::size_t sent = 0;

	while (sent < len)
	{
		std::size_t remaining = len - sent;
		int chunk = remaining > 0x10000 ? 0x10000 : (int)remaining;
		int n = send(s, buf + sent, chunk, 0);

		if (n == SOCKET_ERROR)
		{
			if (WSAGetLastError() != WSAEWOULDBLOCK || !waitForSocket(s, true, timeoutMs))
				return false;
			continue;
		}
		sent += n;
	}
	return true;
}

// Read exactly len bytes. A stream socket may deliver a small message in pieces, so once
// the first bytes have arrived wait for the rest. Fails at once if nothing was readable.
inline bool recvAll(SOCKET s, char* buf, std::size_t len, int timeoutMs = 1000)
{
	std::size_t got = 0;

	while (got < len)
	{
		int n = recv(s, buf + got, (int)(len - got), 0);

		if (n == 0)
			return false;

		if (n == SOCKET_ERROR)
		{
			if (got == 0 || WSAGetLastError() != WSAEWOULDBLOCK || !waitForSocket(s, false, timeoutMs))
				return false;
			continue;
		}
		got += n;

		if (got < len && !waitForSocket(s, false, timeoutMs))
			return false;
	}
	return true;
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
