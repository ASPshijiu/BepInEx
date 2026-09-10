// A warmed x86_64 function must execute its detour after patching under Rosetta.
#include <stdio.h>
#include <dlfcn.h>

typedef int (*Function)(int);
static Function original;
static int calls;
static int replacement(int value) { ++calls; return original(value) + 100; }
__asm__(".text\n.p2align 4\n_target:\n pushq %rbp\n movq %rsp,%rbp\n"
        "nop\n nop\n nop\n nop\n nop\n nop\n nop\n nop\n nop\n nop\n"
        "leal 1(%rdi),%eax\n popq %rbp\n retq\n");
extern int target(int);
int main(int argc, char **argv) {
  if (argc != 2) return 2;
  void *library = dlopen(argv[1], RTLD_NOW);
  if (!library) { puts(dlerror()); return 2; }
  int (*prepare)(void *, void *, void **) = dlsym(library, "DobbyPrepare");
  int (*commit)(void *) = dlsym(library, "DobbyCommit");
  int (*destroy)(void *) = dlsym(library, "DobbyDestroy");
  if (!prepare || !commit || !destroy) return 2;
  Function volatile call = target;
  if (call(5) != 6) return 1;
  if (prepare(target, replacement, (void **)&original) || !original || commit(target)) return 1;
  int result = call(5);
  if (result != 106 || calls != 1 || original(5) != 6) {
    printf("FAIL: patched function returned %d, callback count %d\n", result, calls);
    return 1;
  }
  if (destroy(target) || call(5) != 6) return 1;
  puts("PASS: warmed function enters detour, trampoline works, undo restores original");
  return 0;
}
