// Checks MemReader does not leak process handles when attaching repeatedly.
// The test attaches to itself by exe name, which is what openFirstProcess("eqgame") does to EQ.

#include "stdafx.h"
#include "MemReader.h"

#include <cstdio>
#include <iostream>
#include <string>

int main()
{
	printf("MemReader process handles\n");

	char path[MAX_PATH];
	GetModuleFileNameA(NULL, path, MAX_PATH);
	std::string exe(path);
	exe = exe.substr(exe.find_last_of("\\/") + 1);

	MemReader mr;

	// One attach first so allocator and loader noise settles.
	if (!mr.openFirstProcess(exe, false))
	{
		printf("  FAIL  could not attach to own process '%s'\n", exe.c_str());
		return 1;
	}

	DWORD before = 0, after = 0;
	GetProcessHandleCount(GetCurrentProcess(), &before);

	// MemReader reports every attach on stdout; keep the test output readable.
	std::streambuf* savedOut = std::cout.rdbuf(NULL);

	const int rounds = 300;
	for (int i = 0; i < rounds; ++i)
	{
		if (!mr.openFirstProcess(exe, false))
		{
			printf("  FAIL  attach %d failed\n", i);
			return 1;
		}
	}

	std::cout.rdbuf(savedOut);
	std::cout.clear();
	GetProcessHandleCount(GetCurrentProcess(), &after);
	long growth = (long)after - (long)before;
	printf("  handles before %lu, after %d attaches %lu (growth %ld)\n", before, rounds, after, growth);

	// A leak shows up as at least one handle per attach.
	if (growth > 5)
	{
		printf("\nFAILED: handles leaked\n");
		return 1;
	}
	printf("  ok    handle count stays flat\n\nPASSED\n");
	return 0;
}
