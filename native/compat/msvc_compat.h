/* Force-included (/FI) into every GoodbyeDPI translation unit when building with MSVC. */
#ifndef DPIAKIO_MSVC_COMPAT_H
#define DPIAKIO_MSVC_COMPAT_H
#define _CRT_SECURE_NO_WARNINGS
#define _CRT_RAND_S
#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include <winsock2.h>
#include <ws2tcpip.h>
#include <stdlib.h>
#include <string.h>
#include <basetsd.h>
#ifndef _SSIZE_T_DEFINED
#define _SSIZE_T_DEFINED
typedef SSIZE_T ssize_t;
#endif
#define __attribute__(x)
#endif
