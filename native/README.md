# native/ — GoodbyeDPI source

`src/` is the C source of [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI) by ValdikSS,
taken from upstream commit `f593a276f9ec753889f80208c6a7c5cf455df94a`, licensed under Apache-2.0 (see `LICENSE`).

## Local modifications (needed to build with MSVC; still valid for GCC)

- `src/dnsredir.c` — `DnsFlushResolverCache` function pointer declared as `INT_PTR (WINAPI *)(void)` and cast from `GetProcAddress`.
- `src/goodbyedpi.c` — `--frag-by-sni` size computed with `char*` arithmetic instead of `void*` arithmetic.

## compat/

MSVC shims force-included by `build-goodbyedpi.ps1`:

- `msvc_compat.h` — `ssize_t`, `__attribute__`, `_CRT_RAND_S`, winsock includes.
- `getopt.h` — minimal `getopt_long`.
- `unistd.h` — `sleep()`.
