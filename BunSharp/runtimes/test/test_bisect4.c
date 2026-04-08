/// test_bisect4.c — Isolate: is it Uint8Array specifically, or arg count?

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include <sys/wait.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

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

static BunClass* register_full_greeter(BunContext* ctx) {
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

static int write_tmp(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    size_t len = strlen(src);
    ssize_t w = write(fd, src, len);
    close(fd);
    return (w == (ssize_t)len);
}

typedef int (*TestFn)(void);

/* Test 1: Two string args (no Uint8Array) */
static int test1_two_string_args(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-1-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', 'extra');\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 2: One string + one number (2 args, no Uint8Array) */
static int test2_string_number_args(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-2-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', 42);\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 3: Three string args */
static int test3_three_args(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-3-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('a', 'b', 'c');\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 4: Only Uint8Array arg (1 arg) */
static int test4_only_uint8array(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-4-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter(new Uint8Array([1]));\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 5: Uint8Array create before new (separate statement) */
static int test5_array_before_new(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-5-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const arr = new Uint8Array([1]);\nconst g = new DemoGreeter('test', arr);\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 6: new Array([1]) instead of Uint8Array */
static int test6_regular_array(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-6-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', [1,2,3]);\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 7: new Object() as 2nd arg */
static int test7_object_arg(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);
    BunClass* k = register_full_greeter(ctx);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-7-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', {a:1});\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 8: Minimal class (1 prop + 1 method) + Uint8Array arg */
static int test8_minimal_with_uint8array(void) {
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue g = bun_global(ctx);

    BunClassPropertyDescriptor props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor methods[] = {
        { "describe", 8, greeter_describe, NULL, 0, 0, 0 },
    };
    BunClassDescriptor desc = {
        "DemoGreeter", 11, props, 1, methods, 1,
        greeter_construct, NULL, 2, NULL, 0, NULL, 0,
    };
    BunClass* k = bun_class_register(ctx, &desc, NULL);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-8-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

/* Test 9: 2 props + 1 method (no static) + Uint8Array arg */
static int test9_2props_method_uint8array(void) {
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
        greeter_construct, NULL, 2, NULL, 0, NULL, 0,
    };
    BunClass* k = bun_class_register(ctx, &desc, NULL);
    if (!k) { bun_destroy(rt); return 0; }
    bun_set(ctx, g, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, k));
    char tmp[128]; snprintf(tmp, 128, "/tmp/bun-b4-9-%ld.mjs", (long)getpid());
    write_tmp(tmp, "const g = new DemoGreeter('test', new Uint8Array([1]));\nconsole.log('ok:', g.name);\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));
    int ok = (result != BUN_EXCEPTION);
    unlink(tmp); bun_destroy(rt); return ok;
}

struct { const char* name; TestFn fn; } tests[] = {
    { " 1: two string args", test1_two_string_args },
    { " 2: string + number args", test2_string_number_args },
    { " 3: three string args", test3_three_args },
    { " 4: only Uint8Array (1 arg)", test4_only_uint8array },
    { " 5: Uint8Array in separate var", test5_array_before_new },
    { " 6: regular Array as 2nd arg", test6_regular_array },
    { " 7: Object as 2nd arg", test7_object_arg },
    { " 8: minimal class + Uint8Array", test8_minimal_with_uint8array },
    { " 9: 2 props + method + Uint8Array", test9_2props_method_uint8array },
};
static const int NUM_TESTS = sizeof(tests) / sizeof(tests[0]);

int main(int argc, char** argv) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
    printf("=== test_bisect4: argument type isolation ===\n\n");
    int pass = 0, fail = 0;
    for (int i = 0; i < NUM_TESTS; i++) {
        printf("--- Test%s ---\n", tests[i].name);
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
