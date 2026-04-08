/// test_bisect3.c — Strip down test_csharp_mimic.c one thing at a time
/// Each test is a variant of the mimic, removing one aspect.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/wait.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

/* ── DemoGreeter callbacks (same as csharp_mimic) ── */
typedef struct { char* name; uint8_t* payload; size_t payload_len; } DemoGreeter;

static void greeter_finalize(void* np, void* ud) {
    (void)ud; DemoGreeter* g = (DemoGreeter*)np;
    if (g) { free(g->name); free(g->payload); free(g); }
}
static BunValue greeter_get_name(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)t; (void)ud; DemoGreeter* g = (DemoGreeter*)np;
    return g && g->name ? bun_string(ctx, g->name, strlen(g->name)) : BUN_UNDEFINED;
}
static void greeter_set_name(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)t; (void)ud; DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return; free(g->name);
    size_t len = 0; g->name = bun_to_utf8(ctx, v, &len);
}
static BunValue greeter_get_payload(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)t; (void)ud; DemoGreeter* g = (DemoGreeter*)np;
    if (!g || !g->payload) return BUN_UNDEFINED;
    uint8_t* copy = (uint8_t*)malloc(g->payload_len);
    memcpy(copy, g->payload, g->payload_len);
    return bun_typed_array(ctx, BUN_UINT8_ARRAY, copy, g->payload_len, (BunFinalizerFn)free, copy);
}
static void greeter_set_payload(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)t; (void)ud; DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return; free(g->payload);
    BunTypedArrayInfo info;
    if (bun_get_typed_array(ctx, v, &info) && info.data) {
        g->payload = (uint8_t*)malloc(info.byte_length);
        memcpy(g->payload, info.data, info.byte_length);
        g->payload_len = info.element_count;
    } else { g->payload = NULL; g->payload_len = 0; }
}
static BunValue greeter_describe(BunContext* ctx, BunValue t, void* np,
    int argc, const BunValue* argv, void* ud) {
    (void)t; (void)argc; (void)argv; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return BUN_UNDEFINED;
    char buf[256];
    snprintf(buf, sizeof(buf), "%s:%zu", g->name ? g->name : "", g->payload_len);
    return bun_string(ctx, buf, strlen(buf));
}
static BunValue greeter_static_get_version(BunContext* ctx, BunValue t, void* ud) {
    (void)t; (void)ud; return bun_string(ctx, "v1", 2);
}
static BunValue greeter_construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)ud;
    DemoGreeter* g = (DemoGreeter*)calloc(1, sizeof(DemoGreeter));
    if (!g) return BUN_UNDEFINED;
    if (argc >= 1 && argv) { size_t len = 0; g->name = bun_to_utf8(ctx, argv[0], &len); }
    if (argc >= 2 && argv) {
        BunTypedArrayInfo info;
        if (bun_get_typed_array(ctx, argv[1], &info) && info.data) {
            g->payload = (uint8_t*)malloc(info.byte_length);
            memcpy(g->payload, info.data, info.byte_length);
            g->payload_len = info.element_count;
        }
    }
    return bun_class_new(ctx, klass, g, greeter_finalize, NULL);
}

/* ── BenchmarkBridge callbacks (same as csharp_mimic) ── */
typedef struct { int counter; } BenchmarkBridge;
static void bench_finalize(void* np, void* ud) { (void)ud; free(np); }
static BunValue bench_construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)argc; (void)argv; (void)ud;
    BenchmarkBridge* b = (BenchmarkBridge*)calloc(1, sizeof(BenchmarkBridge));
    if (!b) return BUN_UNDEFINED;
    return bun_class_new(ctx, klass, b, bench_finalize, NULL);
}
static BunValue bench_get_counter(BunContext* ctx, BunValue t, void* np, void* ud) {
    (void)ctx; (void)t; (void)ud;
    return np ? bun_int32(((BenchmarkBridge*)np)->counter) : BUN_UNDEFINED;
}
static void bench_set_counter(BunContext* ctx, BunValue t, void* np, BunValue v, void* ud) {
    (void)ctx; (void)t; (void)ud;
    if (np) ((BenchmarkBridge*)np)->counter = bun_to_int32(v);
}

static int write_tmp(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    size_t len = strlen(src);
    ssize_t w = write(fd, src, len);
    close(fd);
    return (w == (ssize_t)len);
}

typedef int (*TestFn)(void);

/* Helper: register full DemoGreeter */
static BunClass* register_greeter(BunContext* ctx) {
    static BunClassPropertyDescriptor props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
        { "payload", 7, greeter_get_payload, greeter_set_payload, NULL, 0, 0, 0 },
    };
    static BunClassMethodDescriptor methods[] = {
        { "describe", 8, greeter_describe, NULL, 0, 0, 0 },
    };
    static BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, greeter_static_get_version, NULL, NULL, 1, 0, 0 },
    };
    static BunClassDescriptor desc = {
        "DemoGreeter", 11, props, 2, methods, 1,
        greeter_construct, NULL, 2, statics, 1, NULL, 0,
    };
    return bun_class_register(ctx, &desc, NULL);
}

/* Helper: register BenchmarkBridge */
static BunClass* register_bench(BunContext* ctx) {
    static BunClassPropertyDescriptor props[] = {
        { "counter", 7, bench_get_counter, bench_set_counter, NULL, 0, 0, 0 },
    };
    static BunClassDescriptor desc = {
        "BenchmarkBridge", 15, props, 1, NULL, 0,
        bench_construct, NULL, 0, NULL, 0, NULL, 0,
    };
    return bun_class_register(ctx, &desc, NULL);
}

/* ================================================================
 * Test A: FULL mimic (should crash — baseline)
 * ================================================================ */
static int testA_full_mimic(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClass* k1 = register_greeter(ctx);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-A-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test B: DemoGreeter only — no BenchmarkBridge registered
 * ================================================================ */
static int testB_greeter_only(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClass* k1 = register_greeter(ctx);
    if (!k1) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-B-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test C: Full mimic but JS only does: new DemoGreeter('test') — no Uint8Array
 * ================================================================ */
static int testC_no_uint8array(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClass* k1 = register_greeter(ctx);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-C-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test');\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test D: DemoGreeter without static property
 * ================================================================ */
static int testD_no_static(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
        { "payload", 7, greeter_get_payload, greeter_set_payload, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods[] = {
        { "describe", 8, greeter_describe, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc = {
        "DemoGreeter", 11, props, 2, methods, 1,
        greeter_construct, NULL, 2,
        NULL, 0,  /* no statics */
        NULL, 0,
    };
    BunClass* k1 = bun_class_register(ctx, &desc, NULL);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-D-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test E: DemoGreeter with only 1 property (remove payload)
 * ================================================================ */
static int testE_one_prop(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods[] = {
        { "describe", 8, greeter_describe, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, greeter_static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc = {
        "DemoGreeter", 11, props, 1, methods, 1,
        greeter_construct, NULL, 2,
        statics, 1, NULL, 0,
    };
    BunClass* k1 = bun_class_register(ctx, &desc, NULL);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-E-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test');\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test F: DemoGreeter with no methods (just 2 props + static)
 * ================================================================ */
static int testF_no_methods(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
        { "payload", 7, greeter_get_payload, greeter_set_payload, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor statics[] = {
        { "version", 7, greeter_static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor desc = {
        "DemoGreeter", 11, props, 2, NULL, 0,
        greeter_construct, NULL, 2,
        statics, 1, NULL, 0,
    };
    BunClass* k1 = bun_class_register(ctx, &desc, NULL);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-F-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test');\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================
 * Test G: Use bun_eval_string first (warm-up), then bun_eval_file
 * ================================================================ */
static int testG_warmup_first(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClass* k1 = register_greeter(ctx);
    BunClass* k2 = register_bench(ctx);
    if (!k1 || !k2) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k1));
    bun_set(ctx, g, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, k2));

    /* Warm-up: construct via eval_string first */
    bun_eval_string(ctx, BUN_LITERAL("void new DemoGreeter('warmup')"));

    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b3-G-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.describe());\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt);
    return ok;
}

/* ================================================================ */
struct { const char* name; TestFn fn; } tests[] = {
    { "A: FULL mimic (baseline — should crash)", testA_full_mimic },
    { "B: DemoGreeter only (no BenchmarkBridge)", testB_greeter_only },
    { "C: Full mimic, no Uint8Array in JS", testC_no_uint8array },
    { "D: No static property on DemoGreeter", testD_no_static },
    { "E: DemoGreeter with 1 prop instead of 2", testE_one_prop },
    { "F: DemoGreeter no methods (2 props + static)", testF_no_methods },
    { "G: Warm-up via eval_string first", testG_warmup_first },
};
static const int NUM_TESTS = sizeof(tests) / sizeof(tests[0]);

int main(int argc, char** argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
    printf("=== test_bisect3: strip-down mimic ===\n\n");

    int pass = 0, fail = 0;
    for (int i = 0; i < NUM_TESTS; i++) {
        printf("--- Test %s ---\n", tests[i].name);
        fflush(stdout);
        pid_t pid = fork();
        if (pid == 0) { _exit(tests[i].fn() ? 0 : 1); }
        else if (pid > 0) {
            int status = 0; waitpid(pid, &status, 0);
            if (WIFEXITED(status) && WEXITSTATUS(status) == 0) { printf("[PASS]\n\n"); pass++; }
            else if (WIFSIGNALED(status)) { printf("[CRASH] signal %d\n\n", WTERMSIG(status)); fail++; }
            else { printf("[FAIL] exit %d\n\n", WEXITSTATUS(status)); fail++; }
        } else { perror("fork"); fail++; }
    }
    printf("=== Results: %d/%d passed, %d failed ===\n", pass, pass + fail, fail);
    return fail > 0 ? 1 : 0;
}
