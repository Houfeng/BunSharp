/// test_static_prop.c — Test if static properties trigger the crash.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

typedef struct { int value; } Simple;
static void simple_finalize(void* np, void* ud) { (void)ud; free(np); }

static BunValue simple_get_value(BunContext* ctx, BunValue this_, void* np, void* ud) {
    (void)ctx; (void)this_; (void)ud;
    return np ? bun_int32(((Simple*)np)->value) : BUN_UNDEFINED;
}

static BunValue simple_construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)ud;
    Simple* s = (Simple*)calloc(1, sizeof(Simple));
    if (!s) return BUN_UNDEFINED;
    if (argc >= 1 && argv) s->value = bun_to_int32(argv[0]);
    return bun_class_new(ctx, klass, s, simple_finalize, NULL);
}

/* Static getter */
static BunValue static_get_version(BunContext* ctx, BunValue this_, void* ud) {
    (void)this_; (void)ud;
    return bun_string(ctx, "v1", 2);
}

static int write_tmp(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    ssize_t w = write(fd, src, strlen(src));
    close(fd);
    return w > 0;
}

int main(void) {
    setvbuf(stdout, NULL, _IONBF, 0);

    /* ── Test A: class with instance property only (no static) ── */
    printf("--- Test A: instance property only, no static ---\n");
    {
        BunRuntime* rt = bun_initialize(NULL);
        BunContext* ctx = bun_context(rt);
        BunValue global = bun_global(ctx);

        BunClassPropertyDescriptor props[] = {
            { "value", 5, simple_get_value, NULL, NULL, 1, 0, 0 },
        };
        BunClassDescriptor desc = {
            "TestA", 5, props, 1, NULL, 0,
            simple_construct, NULL, 1,
            NULL, 0, NULL, 0,
        };

        BunClass* cls = bun_class_register(ctx, &desc, NULL);
        bun_set(ctx, global, BUN_LITERAL("TestA"), bun_class_constructor(ctx, cls));

        char tmp[128];
        snprintf(tmp, sizeof(tmp), "/tmp/bun-spa-%ld.mjs", (long)getpid());
        write_tmp(tmp, "const a = new TestA(42); globalThis.__a_v = a.value;\n");

        BunValue r = bun_eval_file(ctx, BUN_CSTR(tmp));
        int v = bun_to_int32(bun_eval_string(ctx, BUN_LITERAL("globalThis.__a_v")));
        printf("  result: %s, value=%d\n", r == BUN_EXCEPTION ? "FAIL" : "OK", v);

        unlink(tmp);
        bun_destroy(rt);
    }

    /* ── Test B: class WITH static property ── */
    printf("--- Test B: instance property + static property ---\n");
    {
        BunRuntime* rt = bun_initialize(NULL);
        BunContext* ctx = bun_context(rt);
        BunValue global = bun_global(ctx);

        BunClassPropertyDescriptor props[] = {
            { "value", 5, simple_get_value, NULL, NULL, 1, 0, 0 },
        };
        BunClassStaticPropertyDescriptor static_props[] = {
            { "version", 7, static_get_version, NULL, NULL, 1, 0, 0 },
        };
        BunClassDescriptor desc = {
            "TestB", 5, props, 1, NULL, 0,
            simple_construct, NULL, 1,
            static_props, 1, NULL, 0,
        };

        BunClass* cls = bun_class_register(ctx, &desc, NULL);
        bun_set(ctx, global, BUN_LITERAL("TestB"), bun_class_constructor(ctx, cls));

        char tmp[128];
        snprintf(tmp, sizeof(tmp), "/tmp/bun-spb-%ld.mjs", (long)getpid());
        write_tmp(tmp, "const b = new TestB(99); globalThis.__b_v = b.value;\n");

        BunValue r = bun_eval_file(ctx, BUN_CSTR(tmp));
        int v = bun_to_int32(bun_eval_string(ctx, BUN_LITERAL("globalThis.__b_v")));
        printf("  result: %s, value=%d\n", r == BUN_EXCEPTION ? "FAIL" : "OK", v);

        unlink(tmp);
        bun_destroy(rt);
    }

    /* ── Test C: TWO classes registered (like C# DemoGreeter + BenchmarkBridge) ── */
    printf("--- Test C: two classes registered, new first in eval_file ---\n");
    {
        BunRuntime* rt = bun_initialize(NULL);
        BunContext* ctx = bun_context(rt);
        BunValue global = bun_global(ctx);

        BunClassPropertyDescriptor props1[] = {
            { "value", 5, simple_get_value, NULL, NULL, 1, 0, 0 },
        };
        BunClassDescriptor desc1 = {
            "ClassOne", 8, props1, 1, NULL, 0,
            simple_construct, NULL, 1,
            NULL, 0, NULL, 0,
        };

        BunClassPropertyDescriptor props2[] = {
            { "value", 5, simple_get_value, NULL, NULL, 1, 0, 0 },
        };
        BunClassDescriptor desc2 = {
            "ClassTwo", 8, props2, 1, NULL, 0,
            simple_construct, NULL, 1,
            NULL, 0, NULL, 0,
        };

        BunClass* cls1 = bun_class_register(ctx, &desc1, NULL);
        BunClass* cls2 = bun_class_register(ctx, &desc2, NULL);
        bun_set(ctx, global, BUN_LITERAL("ClassOne"), bun_class_constructor(ctx, cls1));
        bun_set(ctx, global, BUN_LITERAL("ClassTwo"), bun_class_constructor(ctx, cls2));

        char tmp[128];
        snprintf(tmp, sizeof(tmp), "/tmp/bun-spc-%ld.mjs", (long)getpid());
        write_tmp(tmp, "const c = new ClassOne(77); globalThis.__c_v = c.value;\n");

        BunValue r = bun_eval_file(ctx, BUN_CSTR(tmp));
        int v = bun_to_int32(bun_eval_string(ctx, BUN_LITERAL("globalThis.__c_v")));
        printf("  result: %s, value=%d\n", r == BUN_EXCEPTION ? "FAIL" : "OK", v);

        unlink(tmp);
        bun_destroy(rt);
    }

    /* ── Test D: class with string args in constructor (like DemoGreeter) ── */
    printf("--- Test D: constructor with string arg in eval_file ---\n");
    {
        BunRuntime* rt = bun_initialize(NULL);
        BunContext* ctx = bun_context(rt);
        BunValue global = bun_global(ctx);

        BunClassDescriptor desc = {
            "TestD", 5, NULL, 0, NULL, 0,
            simple_construct, NULL, 1,
            NULL, 0, NULL, 0,
        };

        BunClass* cls = bun_class_register(ctx, &desc, NULL);
        bun_set(ctx, global, BUN_LITERAL("TestD"), bun_class_constructor(ctx, cls));

        char tmp[128];
        snprintf(tmp, sizeof(tmp), "/tmp/bun-spd-%ld.mjs", (long)getpid());
        write_tmp(tmp, "const d = new TestD(123); globalThis.__d = 'ok';\n");

        BunValue r = bun_eval_file(ctx, BUN_CSTR(tmp));
        char* s = bun_to_utf8(ctx, bun_eval_string(ctx, BUN_LITERAL("globalThis.__d")), NULL);
        printf("  result: %s, __d=%s\n", r == BUN_EXCEPTION ? "FAIL" : "OK", s ? s : "(null)");
        free(s);

        unlink(tmp);
        bun_destroy(rt);
    }

    printf("\nDone.\n");
    return 0;
}
