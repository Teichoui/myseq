#pragma once

// Tells whether the offsets in the ini file still read the EverQuest that is running.
//
// The ini's PatchDate is not a reliable sign of that: it can come from whoever supplied the offsets, and need not
// match the modified date of eqgame.exe even when the offsets work. So the server watches what happens instead.
// When it reads a real zone and character name from the game, it saves that eqgame.exe's date as
// VerifiedExeTime. If eqgame.exe later has a different timestamp (the game was patched) and no real data can be read
// from it, the offsets are most likely out of date.

#include <string>
#include <windows.h>

// How long after attaching to a game with a new timestamp the server waits for real data before it warns, so that
// sitting at the login screen after a patch does not raise an alarm.
const DWORD PATCH_GRACE_MS = 45000;

// The modified time of a file down to the second, in UTC, as text like "2026-10-19 20:54:18", or an empty string
// if the file cannot be read. A full timestamp (not just the day) so that two updates on one day differ, and
// the same on every PC whatever its date format.
inline std::string FileModifiedStamp(const std::string& path)
{
	WIN32_FILE_ATTRIBUTE_DATA data = {0};
	if (path.empty() || !GetFileAttributesExA(path.c_str(), GetFileExInfoStandard, &data))
		return std::string();

	SYSTEMTIME st;
	if (!FileTimeToSystemTime(&data.ftLastWriteTime, &st))
		return std::string();

	char text[40];
	_snprintf_s(text, sizeof(text), _TRUNCATE, "%04d-%02d-%02d %02d:%02d:%02d",
		st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
	return std::string(text);
}
// Whether a text read from the game looks like a zone short name or a character name: a few letters, digits and
// the odd underscore, space, hyphen or apostrophe. Wrong offsets tend to give nothing or unprintable bytes.
inline bool LooksLikeName(const std::string& text)
{
	if (text.size() < 2 || text.size() > 64)
		return false;

	bool hasLetter = false;
	for (size_t i = 0; i < text.size(); i++)
	{
		unsigned char c = (unsigned char)text[i];
		bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
		bool other = (c >= '0' && c <= '9') || c == '_' || c == ' ' || c == '-' || c == '\'';
		if (!letter && !other)
			return false;
		if (letter)
			hasLetter = true;
	}
	return hasLetter;
}

// True when the zone and the character name read from the game both look real.
inline bool LooksLikeGameData(const std::string& zone, const std::string& character)
{
	return LooksLikeName(zone) && LooksLikeName(character);
}

// True when EverQuest has changed since the offsets were last seen to work and no real data can be read from it.
//   verifiedStamp  the eqgame.exe timestamp saved when the offsets last read the game (empty if never)
//   exeStamp       the timestamp of the eqgame.exe the server is attached to (empty if unknown)
//   dataSeen       real data has been read from this game process
//   msSinceAttach  how long ago the server attached to this game process
// Nothing is claimed when the server never saw the offsets work (verifiedStamp empty): it cannot tell then.
inline bool OffsetsLookStale(const std::string& verifiedStamp, const std::string& exeStamp, bool dataSeen, DWORD msSinceAttach)
{
	return !exeStamp.empty() && !verifiedStamp.empty() && exeStamp != verifiedStamp && !dataSeen && msSinceAttach >= PATCH_GRACE_MS;
}

// What the Patch line of the server window shows. The line has room for 24 characters or so; the longer advice is
// in the log (see StaleWarning).
inline std::string PatchLine(const std::string& iniDate, bool stale)
{
	return stale ? iniDate + " (EQ patched?)" : iniDate;
}

// The longer warning for the server's log.
inline std::string StaleWarning(const std::string& verifiedStamp, const std::string& exeStamp)
{
	return "EverQuest was updated (" + exeStamp + ", the offsets last worked with " + verifiedStamp +
		") and no data can be read from it. The offsets are probably out of date: use Offset Finder.";
}
