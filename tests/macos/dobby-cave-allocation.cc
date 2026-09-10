// Exercises the real Dobby allocator with a deterministic code-cave fallback.
#include "MemoryAllocator/NearMemoryArena.h"
#include "UserMode/PlatformUtil/ProcessRuntimeUtility.h"
#include "UnifiedInterface/platform.h"
#include <cstdio>
#include <cstring>

alignas(4096) static unsigned char cave[4096];
int OSMemory::PageSize() { return 4096; }
int OSMemory::AllocPageSize() { return 4096; }
void *OSMemory::Allocate(void *, int, MemoryPermission) { return nullptr; }
bool OSMemory::SetPermission(void *, int, MemoryPermission) { return true; }
std::vector<MemoryRegion> ProcessRuntimeUtility::GetProcessMemoryLayout() {
  return {{cave, sizeof(cave), kReadExecute}};
}
int main() {
  auto first = NearMemoryArena::AllocateCodeChunk((addr_t)cave, 1024 * 1024, 32);
  if (!first) return 2;
  // A 31-byte trampoline ends in three zero address bytes, plus unused padding.
  memset(first->address, 0x90, 28);
  unsigned char before[32];
  memcpy(before, first->address, sizeof(before));
  auto second = NearMemoryArena::AllocateCodeChunk((addr_t)cave, 1024 * 1024, 32);
  if (!second) return 2;
  memset(second->address, 0xcc, second->length);
  if (memcmp(before, first->address, sizeof(before))) {
    puts("FAIL: allocating a second cave overwrote the first trampoline");
    return 1;
  }
  if ((addr_t)second->address < (addr_t)first->address + first->length) return 1;
  puts("PASS: code-cave reservations preserve zero bytes and unused padding");
  return 0;
}
