#undef NDEBUG
#include <cassert>
#include <functional>
#include <cstdio>
#define TIZENPSP_ARM_JIT
enum class TaskPriority { NORMAL };
struct ThreadManager {
    int workers;
    int GetNumLooperThreads() const { return workers; }
};
static int dispatched, waited;
struct WaitableCounter { void WaitAndRelease() { ++waited; } };
static WaitableCounter counter;
WaitableCounter *ParallelRangeLoopWaitable(ThreadManager *, const std::function<void(int,int)> &loop,
    int lower, int upper, int, int, TaskPriority) {
    ++dispatched; loop(lower, upper); return &counter;
}
#include "parallel-loop-under-test.h"
int main() {
    ThreadManager one{1}, four{4};
    int calls = 0, sum = 0;
    auto loop = [&](int lower, int upper) { ++calls; for (int i=lower;i<upper;++i)sum+=i; };
    ParallelRangeLoop(&one, loop, 3, 103, 1, 4, TaskPriority::NORMAL);
    assert(calls==1 && sum==5250 && dispatched==0 && waited==0);
    calls=sum=0;
    ParallelRangeLoop(&four, loop, 3, 103, 1, 4, TaskPriority::NORMAL);
    assert(calls==1 && sum==5250 && dispatched==1 && waited==1);
    calls=sum=0;
    ParallelRangeLoop(&four, loop, 3, 5, 8, 4, TaskPriority::NORMAL);
    assert(calls==1 && sum==7 && dispatched==1 && waited==1);
    calls=sum=0;
    ParallelRangeLoop(&one, loop, 3, 3, 1, 4, TaskPriority::NORMAL);
    assert(sum==0 && dispatched==1 && waited==1);
    puts("PASS: actual synchronous parallel-loop function avoids dispatch on one available CPU, preserves range/results and waits for multi-CPU work.");
}
