
#if defined(PE1INJ_EXPORTS) || defined(PE1INJ32_EXPORTS)
#define PE1INJ_API __declspec(dllexport)
#else
#define PE1INJ_API __declspec(dllimport)
#endif

extern "C" PE1INJ_API LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam);
