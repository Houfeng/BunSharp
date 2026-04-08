/// test_bisect2.c — More targeted bisection: isolate constructor_arg_count
/// and other subtle differences from test_csharp_mimic.c

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/wait.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

typedef struct { char* name; int counter; } NativeData;

static void native_finalize(void* np, void* ud) {
    (void)ud;
    NativeData* d = (NativeData*)np;
    if (d) { free(d->name); free(d); }
}
static BunValue get_name(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)t; (void)ud;
    NativeData* d = (NativeData*)np;
    return d && d->name ? bun_string(ctx, d->name, strlen(d->name)) : BUN_UNDEFINED;
}
static void set_name(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)t; (void)ud;
    NativeData* d = (NativeData*)np;
    if (!d) return; free(d->name);
    size_t len = 0; d->name = bun_to_utf8(ctx, v, &len);
}
static BunValue get_counter(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)ctx; (void)t; (void)ud;
    return np ? bun_int32(((NativeData*)np)->counter) : BUN_UNDEFINED;
}
static void set_counter(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)ctx; (void)t; (void)ud;
    if (np) ((NativeData*)np)->counter = bun_to_int32(v);
}
static BunValue method_describe(BunContext* ctx, BunValue t, void* np,
    int argc, const BunValue* argv, void* ud) {
    (void)t; (void)argc; (void)argv; (void)ud;
    NativeData* d = (NativeData*)np;
    if (!d) return BUN_UNDEFINED;
    char buf[128];
    snprintf(buf, sizeof(buf), "%s:%d", d->name ? d->name : "?", d->counter);
    return bun_string(ctx, buf, strlen(buf));
}
static BunValue static_get_version(BunContext* ctx, BunValue t, void* ud) {
    (void)t; (void)ud;
    return bun_string(ctx, "v1", 2);
}
static BunValue construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)ud;
    NativeData* d = (NativeData*)calloc(1, sizeof(NativeData));
    if (!d) return BUN_UNDEFINED;
    if (argc >= 1 && argv) { size_t len = 0; d->name = bun_to_utf8(ctx, argv[0], &len); }
    return bun_class_new(ctx, klass, d, native_finalize, NULL);
}

static int write_tmp(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    size_t len = strlen(src);
    ssize_t w = write(fd, src, len);
    close(fd);
    return (w == (ssize_t)len);
}

/* ================================================================
 * Test A: Like test4 from bisect1, but constructor_arg_count = 2 for ClassA
 * ================================================================ */
static int testA_arg_count_2(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props1[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
        { "counter", 7, get_counter, set_counter, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods1[] = {
        { "describe", 8, method_describe, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor statics1[] = {
        { "version", 7, static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc1 = {
        "ClassA", 6, props1, 2, methods1, 1,
        construct, NULL, 2,  /* ← arg_count = 2 (was 1 in bisect1) */
        statics1, 1, NULL, 0,
    };
    BunClassPropertyDescriptor props2[] = {
        { "counter", 7, get_counter, set_counter, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc2 = {
        "ClassB", 6, props2, 1, NULL, 0, construct, NULL, 0, NULL, 0, NULL, 0,
    };

    BunClass* k1 = bun_class_register(ctx, &desc1, NULL);
    BunClass* k2 = bun_class_register(ctx, &desc2, NULL);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("ClassA"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("ClassB"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-A-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const a = new ClassA('hi');\nconsole.log('ok:', a.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test B: Single class, arg_count = 2 (no second class)
 * ================================================================ */
static int testB_single_arg_count_2(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
        { "counter", 7, get_counter, set_counter, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods[] = {
        { "describe", 8, method_describe, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, props, 2, methods, 1,
        construct, NULL, 2,  /* ← arg_count = 2 */
        statics, 1, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-B-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok:', t.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test C: arg_count = 3 single class
 * ================================================================ */
static int testC_arg_count_3(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, props, 1, NULL, 0,
        construct, NULL, 3,  /* ← arg_count = 3 */
        NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-C-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok:', t.name);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test D: arg_count = 2 minimal class (no static, no method)
 * ================================================================ */
static int testD_minimal_arg2(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, props, 1, NULL, 0,
        construct, NULL, 2,  /* ← arg_count = 2 */
        NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-D-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok:', t.name);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test E: arg_count = 0 everywhere (just check eval_file + new works at all)
 * ================================================================ */
static int testE_arg_count_0(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassDescriptor desc = {
        "TestClass", 9, NULL, 0, NULL, 0,
        construct, NULL, 0,
        NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-E-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass();\nconsole.log('ok');\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test F: arg_count = 1 minimal
 * ================================================================ */
static int testF_arg_count_1(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassDescriptor desc = {
        "TestClass", 9, NULL, 0, NULL, 0,
        construct, NULL, 1,
        NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-F-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok');\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test G: arg_count = 2 minimal (no props, no methods, no statics)
 * ================================================================ */
static int testG_arg_count_2_minimal(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassDescriptor desc = {
        "TestClass", 9, NULL, 0, NULL, 0,
        construct, NULL, 2,
        NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-bisect2-G-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('a', 'b');\nconsole.log('ok');\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================ */
typedef int (*TestFn)(void);
struct { const char* name; TestFn fn; } tests[] = {
    { "A: two classes, first arg_count=2 (all features)", testA_arg_count_2 },
    { "B: single class, arg_count=2 (all features)", testB_single_arg_count_2 },
    { "C: single class, arg_count=3 (1 prop)", testC_arg_count_3 },
    { "D: single class, arg_count=2 (1 prop only)", testD_minimal_arg2 },
    { "E: minimal class, arg_count=0", testE_arg_count_0 },
    { "F: minimal class, arg_count=1", testF_arg_count_1 },
    { "G: minimal class, arg_count=2 (no props/methods)", testG_arg_count_2_minimal },
};
static const int NUM_TESTS = sizeof(tests) / sizeof(tests[0]);

int main(int argc, char** argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);

    printf("=== test_bisect2: arg_count and combination bisection ===\n\n");

    int pass = 0, fail = 0;
    for (int i = 0; i < NUM_TESTS; i++) {
        printf("--- Test %s ---\n", tests[i].name);
        fflush(stdout);

        pid_t pid = fork();
        if (pid == 0) {
            int ok = tests[i].fn();
            _exit(ok ? 0 : 1);
        } else if (pid > 0) {
            int status = 0;
            waitpid(pid, &status, 0);
            if (WIFEXITED(status) && WEXITSTATUS(status) == 0) {
                printf("[PASS]\n\n"); pass++;
            } else if (WIFSIGNALED(status)) {
                printf("[CRASH] signal %d\n\n", WTERMSIG(status)); fail++;
            } else {
                printf("[FAIL] exit %d\n\n", WEXITSTATUS(status)); fail++;
            }
        } else { perror("fork"); fail++; }
    }

    printf("=== Results: %d/%d passed, %d failed ===\n", pass, pass + fail, fail);
    return fail > 0 ? 1 : 0;
}
