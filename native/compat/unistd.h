/* Minimal unistd.h shim so GoodbyeDPI builds with MSVC. */
#ifndef DPIAKIO_UNISTD_H
#define DPIAKIO_UNISTD_H
#include <windows.h>
#include <basetsd.h>
#ifndef _SSIZE_T_DEFINED
#define _SSIZE_T_DEFINED
typedef SSIZE_T ssize_t;
#endif
#define sleep(s) Sleep((DWORD)(s) * 1000)
#endif
