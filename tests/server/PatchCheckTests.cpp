// Tests for server/PatchCheck.h: telling when the offsets are out of date after a game patch.
// Build and run with tests\server\run-tests.ps1.

#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <string>

#include "PatchCheck.h"

static int failures = 0;

#define CHECK(cond, msg) do { \
	if (cond) printf("  ok    %s\n", msg); \
	else { printf("  FAIL  %s\n", msg); ++failures; } \
} while (0)

static bool contains(const std::string& text, const std::string& part)
{
	return text.find(part) != std::string::npos;
}

int main()
{
	printf("LooksLikeGameData\n");
	CHECK(LooksLikeGameData("guildhalllrg", "Lalalala"), "a real zone and character name look real");
	CHECK(LooksLikeGameData("gfaydark", "Sir Lancelot"), "a name with a space looks real");
	CHECK(LooksLikeGameData("poknowledge", "D'Arcy-Lee"), "a name with an apostrophe and a hyphen looks real");
	CHECK(!LooksLikeGameData("", "Lalalala"), "no zone is not real data");
	CHECK(!LooksLikeGameData("guildhalllrg", ""), "no character is not real data");
	CHECK(!LooksLikeGameData("StartUp", ""), "the start-up zone with no character is not real data");
	CHECK(!LooksLikeGameData("gu\x01ild", "Lalalala"), "unprintable bytes in the zone are not real data");
	CHECK(!LooksLikeGameData("guildhalllrg", "\xE9\xFF\x80\x7F"), "unprintable bytes in the name are not real data");
	CHECK(!LooksLikeGameData("1234", "Lalalala"), "a zone with no letters is not real data");
	CHECK(!LooksLikeGameData("a", "Lalalala"), "a one-letter zone is not real data");
	CHECK(!LooksLikeGameData(std::string(100, 'a'), "Lalalala"), "a very long zone is not real data");

	printf("\nOffsetsLookStale\n");
	const DWORD late = PATCH_GRACE_MS + 1;
	CHECK(!OffsetsLookStale("2026-10-19 20:54:18", "2026-10-19 20:54:18", false, late), "the same game as when the offsets worked is not stale");
	CHECK(!OffsetsLookStale("2026-10-19 20:54:18", "2026-11-05 12:00:00", true, late), "a patched game that gives real data is not stale");
	CHECK(!OffsetsLookStale("2026-10-19 20:54:18", "2026-11-05 12:00:00", false, 1000), "a patched game is not stale right after attaching");
	CHECK(!OffsetsLookStale("2026-10-19 20:54:18", "2026-11-05 12:00:00", false, PATCH_GRACE_MS - 1), "nor just before the waiting time is over");
	CHECK(OffsetsLookStale("2026-10-19 20:54:18", "2026-11-05 12:00:00", false, PATCH_GRACE_MS), "a patched game with no data after the waiting time is stale");
	CHECK(OffsetsLookStale("2026-11-05 12:00:00", "2026-11-05 18:30:00", false, late), "a second update on the same day is also noticed");
	CHECK(OffsetsLookStale("2026-10-19 20:54:18", "2026-11-05 12:00:00", false, late), "and stays stale while no data is read");
	CHECK(!OffsetsLookStale("", "2026-11-05 12:00:00", false, late), "nothing is claimed when the offsets were never seen to work");
	CHECK(!OffsetsLookStale("2026-10-19 20:54:18", "", false, late), "nothing is claimed when the game's date is unknown");

	printf("\nPatchLine\n");
	CHECK(PatchLine("10/23/2026", false) == "10/23/2026", "the line is just the ini date when all is well");
	std::string line = PatchLine("10/23/2026", true);
	CHECK(contains(line, "10/23/2026") && contains(line, "EQ patched"), "the line keeps the date and says the game was patched when stale");
	CHECK(line.size() <= 24, "the line fits the server window (24 characters)");

	printf("\nStaleWarning\n");
	std::string warning = StaleWarning("2026-10-19 20:54:18", "2026-11-05 12:00:00");
	CHECK(contains(warning, "2026-10-19 20:54:18") && contains(warning, "2026-11-05 12:00:00"), "the warning names both dates");
	CHECK(contains(warning, "Offset Finder"), "the warning says what to do");

	printf("\nFileModifiedStamp\n");
	char dir[MAX_PATH];
	GetTempPathA(MAX_PATH, dir);
	// A name of its own for each run, so two runs at once cannot get in each other's way.
	char unique[64];
	sprintf_s(unique, "myseq-patchcheck-test-%lu-%lu.exe", GetCurrentProcessId(), GetTickCount());
	std::string path = std::string(dir) + unique;
	HANDLE h = CreateFileA(path.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
	CHECK(h != INVALID_HANDLE_VALUE, "created a test file");
	if (h != INVALID_HANDLE_VALUE)
	{
		SYSTEMTIME st = {};
		st.wYear = 2026; st.wMonth = 11; st.wDay = 5; st.wHour = 12; st.wMinute = 30; st.wSecond = 7;
		FILETIME ft;
		SystemTimeToFileTime(&st, &ft);
		SetFileTime(h, NULL, NULL, &ft);
		CloseHandle(h);
		CHECK(FileModifiedStamp(path) == "2026-11-05 12:30:07", "the stamp is the file's time in UTC, to the second");

		// the same day, a few hours later
		h = CreateFileA(path.c_str(), FILE_WRITE_ATTRIBUTES, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
		st.wHour = 18; st.wMinute = 5; st.wSecond = 0;
		SystemTimeToFileTime(&st, &ft);
		SetFileTime(h, NULL, NULL, &ft);
		CloseHandle(h);
		CHECK(FileModifiedStamp(path) == "2026-11-05 18:05:00", "a later change on the same day gives a different stamp");
		DeleteFileA(path.c_str());
	}
	CHECK(FileModifiedStamp("C:\\no\\such\\folder\\eqgame.exe").empty(), "a missing file gives an empty stamp");
	CHECK(FileModifiedStamp("").empty(), "an empty path gives an empty stamp");
	printf("\n%s (%d failure%s)\n", failures == 0 ? "PASSED" : "FAILED", failures, failures == 1 ? "" : "s");
	return failures == 0 ? 0 : 1;
}
