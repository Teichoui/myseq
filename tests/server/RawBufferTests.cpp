// Tests that Spawn's raw buffer starts zeroed and that string extraction stays inside it.
// Build with AddressSanitizer (see run-tests.ps1) so an out-of-bounds read is reported.

#include "stdafx.h"

// Pull in the standard headers first; they must not see the define below.
#include <string>
#include <vector>
#include <iostream>
#include <fstream>
#include <sstream>
#include <map>
#include <algorithm>
#include <cstdio>
#include <cstring>

// extractRawString is private in Spawn; the test needs to call it directly.
#define private public
#include "Spawn.h"
#undef private

#include <cstdio>
#include <cstring>

static int failures = 0;

#define CHECK(cond, msg) do { \
	if (cond) printf("  ok    %s\n", msg); \
	else { printf("  FAIL  %s\n", msg); ++failures; } \
} while (0)

// Hands back fixed offsets so Spawn::init() can run without an ini file.
class FakeIni : public IniReaderInterface
{
public:
	void openFile(string) {}
	void openConfigFile(string) {}
	string readStringEntry(string, string, bool) { return ""; }
	string readEscapeStrings(string, string) { return ""; }
	bool writeStringEntry(string, string, string, bool) { return true; }

	QWORD readIntegerEntry(string section, string entry, bool)
	{
		if (entry == "EightBitRace") return 1;
		if (entry == "NameOffset") return 0;
		if (entry == "LastNameOffset") return 0x40;
		if (entry == "PrevOffset") return 0xA0;
		if (entry == "NextOffset") return 0xA8;
		return 0x60;	// every other field sits in the middle
	}
};

int main()
{
	printf("Spawn raw buffer\n");

	FakeIni ini;
	Spawn sp;
	sp.init(&ini);

	UINT size = sp.largestOffset;
	printf("  buffer size %u bytes\n", size);

	bool allZero = true;
	for (UINT i = 0; i < size; ++i)
		if (sp.rawBuffer[i] != 0) { allZero = false; break; }
	CHECK(allZero, "a new raw buffer is zero-filled (no uninitialized heap data)");

	// Game memory with no NUL after the name: the string must stop at the end of the buffer.
	memset(sp.rawBuffer, 'A', size);
	string name = sp.extractRawString(Spawn::OT_name);
	CHECK(name.size() == size, "unterminated string is cut at the end of the buffer");

	string last = sp.extractRawString(Spawn::OT_lastname);
	CHECK(last.size() == size - 0x40, "same for a field that starts part way in");

	// Normal case still works.
	memset(sp.rawBuffer, 0, size);
	memcpy(sp.rawBuffer, "Orc_Pawn01", 11);
	CHECK(sp.extractRawString(Spawn::OT_name) == "Orc_Pawn01", "terminated string is read normally");

	printf("\n%s (%d failure%s)\n", failures ? "FAILED" : "PASSED", failures, failures == 1 ? "" : "s");
	return failures ? 1 : 0;
}
