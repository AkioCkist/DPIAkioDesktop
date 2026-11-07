/* Minimal getopt_long for MSVC builds of GoodbyeDPI (GNU-like semantics, no permutation). */
#ifndef DPIAKIO_GETOPT_H
#define DPIAKIO_GETOPT_H
#include <stdio.h>
#include <string.h>

#define no_argument       0
#define required_argument 1
#define optional_argument 2

struct option {
    const char *name;
    int has_arg;
    int *flag;
    int val;
};

static char *optarg = NULL;
static int optind = 1, opterr = 1, optopt = 0;
static int dpiakio_optpos = 1;

static int getopt_long(int argc, char * const argv[], const char *optstring,
                       const struct option *longopts, int *longindex)
{
    optarg = NULL;
    if (optind >= argc) return -1;
    char *arg = argv[optind];
    if (arg[0] != '-' || arg[1] == '\0') return -1;

    if (arg[1] == '-') {
        if (arg[2] == '\0') { optind++; return -1; } /* "--" */
        const char *name = arg + 2;
        const char *eq = strchr(name, '=');
        size_t len = eq ? (size_t)(eq - name) : strlen(name);
        optind++;
        for (int i = 0; longopts && longopts[i].name; i++) {
            if (strlen(longopts[i].name) != len || strncmp(longopts[i].name, name, len) != 0)
                continue;
            if (longindex) *longindex = i;
            if (longopts[i].has_arg == no_argument) {
                if (eq) { if (opterr) fprintf(stderr, "option '--%s' doesn't allow an argument\n", longopts[i].name); return '?'; }
            } else if (eq) {
                optarg = (char *)eq + 1;
            } else if (longopts[i].has_arg == required_argument) {
                if (optind >= argc) { if (opterr) fprintf(stderr, "option '--%s' requires an argument\n", longopts[i].name); return '?'; }
                optarg = argv[optind++];
            }
            if (longopts[i].flag) { *longopts[i].flag = longopts[i].val; return 0; }
            return longopts[i].val;
        }
        if (opterr) fprintf(stderr, "unrecognized option '%s'\n", arg);
        return '?';
    }

    char c = arg[dpiakio_optpos];
    const char *p = strchr(optstring, c);
    optopt = c;
    if (!p || c == ':') {
        if (opterr) fprintf(stderr, "invalid option -- '%c'\n", c);
        if (arg[++dpiakio_optpos] == '\0') { optind++; dpiakio_optpos = 1; }
        return '?';
    }
    if (p[1] == ':') {
        if (arg[dpiakio_optpos + 1] != '\0') {
            optarg = arg + dpiakio_optpos + 1;
        } else if (p[2] != ':') {
            if (optind + 1 >= argc) {
                if (opterr) fprintf(stderr, "option requires an argument -- '%c'\n", c);
                optind++; dpiakio_optpos = 1;
                return '?';
            }
            optarg = argv[++optind];
        }
        optind++; dpiakio_optpos = 1;
    } else if (arg[++dpiakio_optpos] == '\0') {
        optind++; dpiakio_optpos = 1;
    }
    return c;
}
#endif
