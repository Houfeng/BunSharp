/// test_csharp_mimic.c — Mimics the exact C# DemoGreeter registration pattern
/// to find what C# does differently that causes the crash.

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <unistd.h>
#include "../headers/bun_embed.h"

#define BUN_LITERAL(s) (s), (sizeof(s) - 1)
#define BUN_CSTR(s) (s), strlen(s)

/* ── Mimic DemoGreeter: string name, byte[] payload ── */
typedef struct {
    char* name;
    uint8_t* payload;
    size_t payload_len;
} DemoGreeter;

static void greeter_finalize(void* np, void* ud) {
    (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (g) {
        free(g->name);
        free(g->payload);
        free(g);
    }
}

static BunValue greeter_get_name(BunContext* ctx, BunValue this_, void* np, void* ud) {
    (void)this_; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    return g && g->name ? bun_string(ctx, g->name, strlen(g->name)) : BUN_UNDEFINED;
}

static void greeter_set_name(BunContext* ctx, BunValue this_, void* np, BunValue v, void* ud) {
    (void)ctx; (void)this_; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return;
    free(g->name);
    size_t len = 0;
    char* s = bun_to_utf8(ctx, v, &len);
    g->name = s;  /* take ownership */
}

static BunValue greeter_get_payload(BunContext* ctx, BunValue this_, void* np, void* ud) {
    (void)this_; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (!g || !g->payload) return BUN_UNDEFINED;
    /* Return a copy as Uint8Array */
    uint8_t* copy = (uint8_t*)malloc(g->payload_len);
    memcpy(copy, g->payload, g->payload_len);
    return bun_typed_array(ctx, BUN_UINT8_ARRAY, copy, g->payload_len, (BunFinalizerFn)free, copy);
}

static void greeter_set_payload(BunContext* ctx, BunValue this_, void* np, BunValue v, void* ud) {
    (void)this_; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return;
    free(g->payload);
    BunTypedArrayInfo info;
    if (bun_get_typed_array(ctx, v, &info) && info.data) {
        g->payload = (uint8_t*)malloc(info.byte_length);
        memcpy(g->payload, info.data, info.byte_length);
        g->payload_len = info.element_count;
    } else {
        g->payload = NULL;
        g->payload_len = 0;
    }
}

static BunValue greeter_describe(BunContext* ctx, BunValue this_, void* np,
    int argc, const BunValue* argv, void* ud) {
    (void)this_; (void)argc; (void)argv; (void)ud;
    DemoGreeter* g = (DemoGreeter*)np;
    if (!g) return BUN_UNDEFINED;
    char buf[256];
    snprintf(buf, sizeof(buf), "%s:%zu", g->name ? g->name : "", g->payload_len);
    return bun_string(ctx, buf, strlen(buf));
}

/* Static property: version */
static BunValue greeter_static_get_version(BunContext* ctx, BunValue this_, void* ud) {
    (void)this_; (void)ud;
    return bun_string(ctx, "v1", 2);
}

static BunValue greeter_construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)ud;
    DemoGreeter* g = (DemoGreeter*)calloc(1, sizeof(DemoGreeter));
    if (!g) return BUN_UNDEFINED;

    /* arg 0: string name */
    if (argc >= 1 && argv) {
        size_t len = 0;
        g->name = bun_to_utf8(ctx, argv[0], &len);
    }
    /* arg 1: Uint8Array payload */
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

/* ── Mimic BenchmarkBridge (empty constructor, minimal) ── */
typedef struct {
    int counter;
    char* text;
    uint8_t* buffer;
    size_t buffer_len;
} BenchmarkBridge;

static void bench_finalize(void* np, void* ud) {
    (void)ud;
    BenchmarkBridge* b = (BenchmarkBridge*)np;
    if (b) {
        free(b->text);
        free(b->buffer);
        free(b);
    }
}

static BunValue bench_construct(BunContext* ctx, BunClass* klass,
    int argc, const BunValue* argv, void* ud) {
    (void)argc; (void)argv; (void)ud;
    BenchmarkBridge* b = (BenchmarkBridge*)calloc(1, sizeof(BenchmarkBridge));
    if (!b) return BUN_UNDEFINED;
    return bun_class_new(ctx, klass, b, bench_finalize, NULL);
}

static BunValue bench_get_counter(BunContext* ctx, BunValue this_, void* np, void* ud) {
    (void)ctx; (void)this_; (void)ud;
    return np ? bun_int32(((BenchmarkBridge*)np)->counter) : BUN_UNDEFINED;
}

static void bench_set_counter(BunContext* ctx, BunValue this_, void* np, BunValue v, void* ud) {
    (void)ctx; (void)this_; (void)ud;
    if (np) ((BenchmarkBridge*)np)->counter = bun_to_int32(v);
}

/* ── Write temp module ── */
static int write_tmp_module(const char* path, const char* src) {
    int fd = open(path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (fd < 0) return 0;
    size_t len = strlen(src);
    ssize_t wrote = write(fd, src, len);
    close(fd);
    return (wrote == (ssize_t)len);
}

int main(void) {
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);

    printf("=== test_csharp_mimic: replicate C# DemoGreeter + BenchmarkBridge ===\n\n");

    /* ── Step 1: Same as C# - no debugger, with Cwd ── */
    BunRuntime* rt = bun_initialize(NULL);
    BunContext* ctx = bun_context(rt);
    BunValue global = bun_global(ctx);

    /* ── Register DemoGreeter (2 props, 1 method, 1 static prop, constructor with 2 args) ── */
    BunClassPropertyDescriptor greeter_props[] = {
        { "name", 4, greeter_get_name, greeter_set_name, NULL, 0, 0, 0 },
        { "payload", 7, greeter_get_payload, greeter_set_payload, NULL, 0, 0, 0 },
    };
    BunClassMethodDescriptor greeter_methods[] = {
        { "describe", 8, greeter_describe, NULL, 0, 0, 0 },
    };
    BunClassStaticPropertyDescriptor greeter_static_props[] = {
        { "version", 7, greeter_static_get_version, NULL, NULL, 1, 0, 0 },
    };
    BunClassDescriptor greeter_desc = {
        "DemoGreeter", 11,
        greeter_props, 2,
        greeter_methods, 1,
        greeter_construct, NULL, 2,
        greeter_static_props, 1,
        NULL, 0,
    };

    BunClass* greeter_class = bun_class_register(ctx, &greeter_desc, NULL);
    if (!greeter_class) {
        fprintf(stderr, "FAIL: bun_class_register DemoGreeter returned NULL\n");
        bun_destroy(rt);
        return 1;
    }
    printf("DemoGreeter class registered OK\n");

    /* ── Register BenchmarkBridge ── */
    BunClassPropertyDescriptor bench_props[] = {
        { "counter", 7, bench_get_counter, bench_set_counter, NULL, 0, 0, 0 },
    };
    BunClassDescriptor bench_desc = {
        "BenchmarkBridge", 15,
        bench_props, 1,
        NULL, 0,
        bench_construct, NULL, 0,
        NULL, 0,
        NULL, 0,
    };

    BunClass* bench_class = bun_class_register(ctx, &bench_desc, NULL);
    if (!bench_class) {
        fprintf(stderr, "FAIL: bun_class_register BenchmarkBridge returned NULL\n");
        bun_destroy(rt);
        return 1;
    }
    printf("BenchmarkBridge class registered OK\n");

    /* ── Set constructors on global (same as C# PublishGlobal) ── */
    bun_set(ctx, global, BUN_LITERAL("DemoGreeter"), bun_class_constructor(ctx, greeter_class));
    bun_set(ctx, global, BUN_LITERAL("BenchmarkBridge"), bun_class_constructor(ctx, bench_class));
    printf("Constructors published to global\n");

    /* ── Write test module ── */
    char tmp[128];
    snprintf(tmp, sizeof(tmp), "/tmp/bun-mimic-%ld.mjs", (long)getpid());

    const char* src =
        "const g = new DemoGreeter('test', new Uint8Array([1]));\n"
        "console.log('describe:', g.describe());\n";

    if (!write_tmp_module(tmp, src)) {
        fprintf(stderr, "FAIL: cannot write module\n");
        bun_destroy(rt);
        return 1;
    }

    /* ── bun_eval_file — the crash point ── */
    printf("About to bun_eval_file...\n");
    BunValue result = bun_eval_file(ctx, BUN_CSTR(tmp));

    if (result == BUN_EXCEPTION) {
        fprintf(stderr, "FAIL: bun_eval_file threw: %s\n", bun_last_error(ctx, NULL));
    } else {
        printf("[PASS] bun_eval_file completed successfully\n");
    }

    unlink(tmp);
    bun_destroy(rt);
    printf("Done.\n");
    return 0;
}
