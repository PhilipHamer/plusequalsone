// pe1inj.cpp : Defines the exported functions for the DLL application.
//

#include <SDKDDKVer.h>
#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
#include <windows.h>
#include "pe1inj.h"
#define PE1LOG_PREFIX "PE1INJ"
#include "pe1common.h"


HINSTANCE g_hDll = 0;
UINT g_msgHook = 0;
UINT g_msgTextChange = 0;
UINT g_msgSetNotepadId = 0;
UINT g_msgPing = 0;
UINT g_msgCheckWasHooked = 0;
HWND g_hWndNotepad = 0;
HWND g_hWndExternal = 0;
BOOL g_bSubclassed = FALSE;
int g_notepadId = 0; // unique id that survives across restarts
int g_pid = 0;
int g_informNotepadId = 0;
DWORD g_lastPing = 0;
WNDPROC OldProc = NULL;
LRESULT CALLBACK NewProc(HWND, UINT, WPARAM, LPARAM);


BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID /*lpReserved*/)
{
	if (ul_reason_for_call == DLL_PROCESS_ATTACH)
	{
		g_hDll = (HINSTANCE)hModule;
		::DisableThreadLibraryCalls(g_hDll);
		g_msgHook = ::RegisterWindowMessageW(L"WM_PE1_HOOK");
		g_msgTextChange = ::RegisterWindowMessageW(L"WM_PE1_TEXTCHANGE");
		g_msgSetNotepadId = ::RegisterWindowMessageW(L"WM_PE1_SETNOTEPADID");
		g_msgPing = ::RegisterWindowMessageW(L"WM_PE1_PING");
		g_msgCheckWasHooked = ::RegisterWindowMessageW(L"WM_PE1_CHECKWASHOOKED");
	}

	return TRUE;
}

int MakeNotepadId()
{
	int pid = ::GetCurrentProcessId();
	int time = 0;
	FILETIME ftCreation = { 0 }, ft2, ft3, ft4;
	if (::GetProcessTimes(::GetCurrentProcess(), &ftCreation, &ft2, &ft3, &ft4))
	{
		SYSTEMTIME stCreation = { 0 };
		if (::FileTimeToSystemTime(&ftCreation, &stCreation))
		{
			time = stCreation.wSecond * 1000 + stCreation.wMilliseconds;
		}
		else
		{
			LOGERROR("FileTimeToSystemTime failed");
		}
	}
	else
	{
		LOGERROR("GetProcessTimes failed");
	}
	return (pid << 16) | time;
}

LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
{
	CWPSTRUCT* pCWP = (CWPSTRUCT*)lParam;
	if (pCWP->message == g_msgHook && pCWP->lParam)
	{
		LOG("hook start");

		HHOOK hHook = (HHOOK)pCWP->wParam;
		if (!::UnhookWindowsHookEx(hHook))
		{
			LOGERROR("UnhookWindowsHookEx failed");
		}

		g_lastPing = ::GetTickCount();
		g_informNotepadId = 0;

		if (g_bSubclassed)
		{
			LOG("already subclassed");
			// now we have to do a bit of handshaking to reacquaint ourselves
			g_hWndExternal = (HWND)pCWP->lParam; // update window of possible new external process
			g_informNotepadId = g_notepadId; // communicate possibly non-obvious notepad id back to external process
			goto END;
		}

		// increase ref count of DLL so that it's not unloaded
		wchar_t lib_name[MAX_PATH];
		::GetModuleFileNameW(g_hDll, lib_name, MAX_PATH);
		if (!::LoadLibrary(lib_name))
		{
			LOGERROR("LoadLibrary failed");
			goto END;
		}

		g_hWndNotepad = pCWP->hwnd;
		g_hWndExternal = (HWND)pCWP->lParam;
		g_pid = ::GetCurrentProcessId();
		// we should soon get a msg with our notepad id, but until then we can calculate it ourselves
		g_notepadId = MakeNotepadId();

		OldProc = (WNDPROC)::SetWindowLongPtr(g_hWndNotepad, GWLP_WNDPROC, (LONG_PTR)NewProc);
		if (!OldProc)
		{
			LOGERROR("SetWindowLongPtr failed");
			::FreeLibrary(g_hDll);
		}
		else
		{
			g_bSubclassed = true;
			LOG("hook succeeded");
		}
	}
	else if (pCWP->message == g_msgHook)
	{
		LOG("unhook start");

		HHOOK hHook = (HHOOK)pCWP->wParam;
		if (!::UnhookWindowsHookEx(hHook))
		{
			LOGERROR("UnhookWindowsHookEx failed");
		}

		if (g_bSubclassed && g_hWndNotepad)
		{
			if (!::SetWindowLongPtr(g_hWndNotepad, GWLP_WNDPROC, (LONG_PTR)OldProc))
			{
				LOGERROR("SetWindowLongPtr failed");
				goto END;
			}
		}
		else
		{
			LOG("SetWindowLongPtr not attempted");
		}

		::FreeLibrary(g_hDll);
		g_bSubclassed = false;
		g_informNotepadId = 0;
		LOG("unhooked");
	}

END:
	return ::CallNextHookEx(NULL, code, wParam, lParam);
}


LRESULT CALLBACK NewProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	const int ACK = 104;

	if (uMsg == g_msgHook)
	{
		return ACK;
	}

	if (uMsg == g_msgCheckWasHooked)
	{
		return g_informNotepadId ? g_informNotepadId : ACK;
	}

	if (uMsg == g_msgSetNotepadId)
	{
		g_notepadId = (int)wParam;
		LOG("notepad id set");
		return ACK;
	}

	if (uMsg == g_msgPing)
	{
		g_lastPing = ::GetTickCount();
		return ACK;
	}

	if (uMsg == WM_QUERYENDSESSION)
	{
		if (::GetTickCount() - g_lastPing < 10000)
		{
			// we're being monitored by our external app, so no need to bother the user with a Yes/No/Cancel from notepad
			return 1;
		}
	}

	if (uMsg == WM_COMMAND && HIWORD(wParam) == EN_CHANGE)
	{
		::PostMessage(g_hWndExternal, g_msgTextChange, g_pid, g_notepadId);
	}

	return CallWindowProc(OldProc, hwnd, uMsg, wParam, lParam);
}
