// pe1wow.cpp : Defines the entry point for the console application.
//

#include <SDKDDKVer.h>
#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#include <windows.h>
#define PE1LOG_PREFIX "PE1WOW"
#include "pe1common.h"



int wmain(int argc, wchar_t* argv[])
{
	enum {
		ERR_ARGS = 1,
		ERR_HWND,
		ERR_LOADLIB,
		ERR_GETPROCADDR,
		ERR_REGWNDMSG,
		ERR_THREADID,
		ERR_SETWINHOOK,
		ERR_SENDMSG,
		//...
		ERR_UNKNOWN = 99
	};

	const int ACK = 104;

	// HWND of notepad main window is our first argument.
	// HWND of external app calling us is our second argument.
	// they should be passed as 32-bit signed int.
	if (argc < 3)
	{
		LOG("not enough arguments");
		return ERR_ARGS;
	}
	HWND hwndNotepad = (HWND)_wtoi(argv[1]);
	HWND hwndExternal = (HWND)_wtoi(argv[2]);
	if (!hwndNotepad || !hwndExternal)
	{
		LOG("could not interpret hwnd from arguments");
		return ERR_HWND;
	}

	HMODULE hMod = ::LoadLibraryW(L"pe1inj32.dll");
	if (!hMod)
	{
		LOGERROR("failed to load pe1inj32.dll");
		return ERR_LOADLIB;
	}
	FARPROC proc = ::GetProcAddress(hMod, "HookProc");
	if (!proc)
	{
		LOGERROR("failed to find HookProc");
		return ERR_GETPROCADDR;
	}

	UINT hookMsg = ::RegisterWindowMessageW(L"WM_PE1_HOOK");
	if (!hookMsg)
	{
		LOGERROR("could not register WM_PE1_HOOK");
		return ERR_REGWNDMSG;
	}

	DWORD pid = 0;
	DWORD tid = ::GetWindowThreadProcessId(hwndNotepad, &pid);
	if (!tid)
	{
		LOG("could not find thread id for notepad main window");
		return ERR_THREADID;
	}

	// BUG: using SetWindowsHookExW instead of SetWindowsHookExA on Windows 8 (for hooking wow notepads on x64) causes a crash.
	// the crash occurs in the wow64 notepad.exe process when SendMessage is called and results in eip=<hookMsg>.
	// it would be nice (and fun!) to track this down, but for now i'll just use SetWindowsHookExA and be done with it.
	HHOOK hook = ::SetWindowsHookExA(WH_CALLWNDPROC, (HOOKPROC)proc, hMod, tid);
	if (!hook)
	{
		LOGERROR("failed to hook wow64 notepad");
		return ERR_SETWINHOOK;
	}

	LRESULT result = ::SendMessageW(hwndNotepad, hookMsg, (WPARAM)hook, (LPARAM)hwndExternal);
	if (result != ACK)
	{
		LOG("hook message failed");
		return ERR_SENDMSG;
	}

	return 0;
}

