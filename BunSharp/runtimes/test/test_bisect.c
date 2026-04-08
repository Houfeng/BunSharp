/// test_bisect.c — Systematically bisect which class feature combination
/// triggers the segfault in bun_eval_file + constructor call.
///
/// Usage: ./test_bisect [test_number]
///   0 = run all, 1..N = run specific test

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

/* ── Shared native struct ── */
typedef struct { char* name; int counter; } NativeData;

static void native_finalize(void* np, void* ud) {
    (void)ud;
    NativeData* d = (NativeData*)np;
    if (d) { free(d->name); free(d); }
}

/* ── Property callbacks ── */
static BunValue get_name(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)t; (void)ud;
    NativeData* d = (NativeData*)np;
    return d && d->name ? bun_string(ctx, d->name, strlen(d->name)) : BUN_UNDEFINED;
}
static void set_name(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)t; (void)ud;
    NativeData* d = (NativeData*)np;
    if (!d) return;
    free(d->name);
    size_t len = 0;
    d->name = bun_to_utf8(ctx, v, &len);
}
static BunValue get_counter(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)ctx; (void)t; (void)ud;
    return np ? bun_int32(((NativeData*)np)->counter) : BUN_UNDEFINED;
}
static void set_counter(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)ctx; (void)t; (void)ud;
    if (np) ((NativeData*)np)->counter = bun_to_int32(v);
}

/* ── Method callback ── */
static BunValue method_describe(BunContext* ctx, BunValue t, void* np,
    int argc, const BunValue* argv, void* ud) {
    (void)t; (void)argc; (void)argv; (void)ud;
    NativeData* d = (NativeData*)np;
    if (!d) return BUN_UNDEFINED;
    char buf[128];
    snprintf(buf, sizeof(buf), "%s:%d", d->name ? d->name : "?", d->counter);
    return bun_string(ctx, buf, strlen(buf));
}

/* ── Static property callback ── */
static BunValue static_get_version(BunContext* ctx, BunValue t, void* ud) {
    (void)t; (void)ud;
    return bun_string(ctx, "v1", 2);
}

/* ── Generic constructor ── */
static BunValue construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)ud;
    NativeData* d = (NativeData*)calloc(1, sizeof(NativeData));
    if (!d) return BUN_UNDEFINED;
    if (argc >= 1 && argv) {
        size_t len = 0;
        d->name = bun_to_utf8(ctx, argv[0], &len);
    }
    return bun_class_new(ctx, klass, d, native_finalize, NULL);
}

/* ── Helper ── */
static int write_tmp(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    size_t len = strlen(src);
    ssize_t w = write(fd, src, len);
    close(fd);
    return (w == (ssize_t)len);
}

typedef int (*TestFn)(void);

/* ================================================================
 * Test 1: One class, 2 instance props + 1 method + 1 static prop
 *         (full DemoGreeter-like, but only one class)
 * ================================================================ */
static int test1_full_single_class(void) {
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
        "TestClass", 9,
        props, 2, methods, 1,
        construct, NULL, 1,
        statics, 1, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-1-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hello');\nconsole.log('ok:', t.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 2: Two classes, each with 1 instance prop only (minimal)
 * ================================================================ */
static int test2_two_minimal_classes(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props1[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc1 = {
        "ClassA", 6, props1, 1, NULL, 0, construct, NULL, 1, NULL, 0, NULL, 0,
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

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-2-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const a = new ClassA('hi');\nconsole.log('ok:', a.name);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 3: Two classes, first has 2 props + method, second has 1 prop
 * ================================================================ */
static int test3_two_classes_mixed(void) {
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
    BunClassDescriptor desc1 = {
        "ClassA", 6, props1, 2, methods1, 1, construct, NULL, 1, NULL, 0, NULL, 0,
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

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-3-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const a = new ClassA('hi');\nconsole.log('ok:', a.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 4: Two classes, first has 2 props + method + static, second has 1 prop
 *         (This is closest to the C# combination)
 * ================================================================ */
static int test4_two_classes_with_static(void) {
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
        "ClassA", 6, props1, 2, methods1, 1, construct, NULL, 1, statics1, 1, NULL, 0,
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

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-4-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const a = new ClassA('hi');\nconsole.log('ok:', a.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 5: One class with static prop ONLY (no instance props/methods)
 * ================================================================ */
static int test5_static_only(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, NULL, 0, NULL, 0, construct, NULL, 0, statics, 1, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-5-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass();\nconsole.log('ok:', TestClass.version);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 6: One class, 1 instance prop + 1 static prop (2 descriptor types)
 * ================================================================ */
static int test6_inst_plus_static(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, props, 1, NULL, 0, construct, NULL, 1, statics, 1, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-6-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok:', t.name, TestClass.version);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 7: One class, 1 instance prop + 1 method (no static)
 * ================================================================ */
static int test7_inst_plus_method(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, get_name, set_name, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods[] = {
        { "describe", 8, method_describe, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc = {
        "TestClass", 9, props, 1, methods, 1, construct, NULL, 1, NULL, 0, NULL, 0,
    };

    BunClass* klass = bun_class_register(ctx, &desc, NULL);
    if (!klass) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("TestClass"), bun_class_constructor(ctx, klass));

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-7-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const t = new TestClass('hi');\nconsole.log('ok:', t.describe());\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test 8: One class, 2 instance props + 1 method + 1 static (full, again
 *         but with a string arg to constructor)
 *         Same as test1 but construct passes string arg.
 * ================================================================ */
/* already covered by test1 */

/* ================================================================
 * Test 9: Same as test4 but instantiate ClassB (second class) instead
 * ================================================================ */
static int test9_two_classes_construct_second(void) {
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
        "ClassA", 6, props1, 2, methods1, 1, construct, NULL, 1, statics1, 1, NULL, 0,
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

    char tmp[64]; snprintf(tmp, 64, "/tmp/bun-bisect-9-%ld.mjs", (long)getpid());
    /* Instantiate ClassB, not ClassA */
    write_tmp(tmp, "const b = new ClassB();\nconsole.log('ok:', b.counter);\n");

    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp);
    bun_destroy(rt);
    return ok;
}

/* ================================================================ */
struct { const char* name; TestFn fn; } tests[] = {
    { "1: full single class (2 props + method + static)", test1_full_single_class },
    { "2: two minimal classes (1 prop each)", test2_two_minimal_classes },
    { "3: two classes (first: 2 props + method, second: 1 prop)", test3_two_classes_mixed },
    { "4: two classes (first: 2 props + method + static, second: 1 prop)", test4_two_classes_with_static },
    { "5: single class with static prop only", test5_static_only },
    { "6: single class (1 instance prop + 1 static prop)", test6_inst_plus_static },
    { "7: single class (1 instance prop + 1 method)", test7_inst_plus_method },
    { "9: two classes with static — construct second class", test9_two_classes_construct_second },
};
static const int NUM_TESTS = sizeof(tests) / sizeof(tests[0]);

int main(int argc, char** argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);

    int which = 0; /* 0 = all */
    if (argc > 1) which = atoi(argv[1]);

    printf("=== test_bisect: class feature combination bisection ===\n\n");

    int pass = 0, fail = 0;
    for (int i = 0; i < NUM_TESTS; i++) {
        int test_num = i + 1;
        /* Skip if specific test requested */
        if (which > 0) {
            /* parse the test number from the name */
            int tn = 0;
            sscanf(tests[i].name, "%d:", &tn);
            if (tn != which) continue;
        }

        printf("--- Test %s ---\n", tests[i].name);
        fflush(stdout);

        /* Fork so crash doesn't kill the runner */
        pid_t pid = fork();
        if (pid == 0) {
            /* Child — run test */
            int ok = tests[i].fn();
            _exit(ok ? 0 : 1);
        } else if (pid > 0) {
            int status = 0;
            waitpid(pid, &status, 0);
            if (WIFEXITED(status) && WEXITSTATUS(status) == 0) {
                printf("[PASS]\n\n");
                pass++;
            } else if (WIFSIGNALED(status)) {
                printf("[CRASH] signal %d\n\n", WTERMSIG(status));
                fail++;
            } else {
                printf("[FAIL] exit %d\n\n", WEXITSTATUS(status));
                fail++;
            }
        } else {
            perror("fork");
            fail++;
        }
    }

    printf("=== Results: %d/%d passed, %d failed ===\n", pass, pass + fail, fail);
    return fail > 0 ? 1 : 0;
}
