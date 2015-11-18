
#pragma once

#include <string>

#ifndef PE1LOG_PREFIX
#define PE1LOG_PREFIX "PE1"
#endif

#define _PE1_PFX_GEN(x)		_PE1_PFX_GEN2(x)
#define _PE1_PFX_GEN2(x)	L##x
#define _PE1_PFX			_PE1_PFX_GEN(PE1LOG_PREFIX)

#define LOG(msg) ::OutputDebugStringW(_PE1_PFX L" " L##msg L"\n")
#define LOGERROR(msg) \
	do { \
		::OutputDebugStringW(_PE1_PFX L" " L##msg L"("); \
		::OutputDebugStringW(std::to_wstring(::GetLastError()).c_str()); \
		::OutputDebugStringW(L")\n"); \
	__pragma(warning(suppress:4127)) \
	} while (0)

