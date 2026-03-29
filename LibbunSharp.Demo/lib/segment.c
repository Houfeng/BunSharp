#ifdef _MSC_VER
    #pragma data_seg("MY_BLOB")
    static char placeholder = { 0 };
    #pragma data_seg()
#else
    __attribute__((section("MY_BLOB")))
    static char placeholder = { 0 };
#endif