using System;
using NUnit.Framework;
using Unity.Collections;
using Core.Collections;
using Core.Data;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for NativeMinHeap - the priority queue behind A* pathfinding.
    ///
    /// WHY THIS MATTERS: if the heap ever returns something other than the true minimum,
    /// A* stops being optimal and silently returns longer paths. That reads as an AI
    /// quality problem rather than a data structure bug, so it can hide for a long time.
    /// </summary>
    public class NativeMinHeapTests
    {
        private struct IntItem : IComparable<IntItem>
        {
            public int Value;
            public IntItem(int value) => Value = value;
            public int CompareTo(IntItem other) => Value.CompareTo(other.Value);
        }

        // ===== Ordering =====

        [Test]
        public void Pop_ReturnsElementsInAscendingOrder()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            foreach (var value in new[] { 5, 3, 8, 1, 9, 2, 7 })
                heap.Push(new IntItem(value));

            var expected = new[] { 1, 2, 3, 5, 7, 8, 9 };
            foreach (var value in expected)
            {
                Assert.AreEqual(value, heap.Pop().Value,
                    "Pop must always return the current minimum");
            }
        }

        /// <summary>
        /// Randomised push/pop against a sorted reference. Interleaves operations so the
        /// heap is not simply filled then drained - that is the pattern A* actually uses,
        /// and it exercises HeapifyUp and HeapifyDown together.
        /// </summary>
        [Test]
        public void Pop_MatchesSortedOrder_UnderRandomisedOperations()
        {
            var random = new Random(20260729);

            for (int trial = 0; trial < 200; trial++)
            {
                using var heap = new NativeMinHeap<IntItem>(8, Allocator.Temp);
                var reference = new System.Collections.Generic.List<int>();

                for (int op = 0; op < 60; op++)
                {
                    bool shouldPush = reference.Count == 0 || random.Next(100) < 60;

                    if (shouldPush)
                    {
                        int value = random.Next(-1000, 1000);
                        heap.Push(new IntItem(value));
                        reference.Add(value);
                    }
                    else
                    {
                        reference.Sort();
                        int expected = reference[0];
                        reference.RemoveAt(0);

                        Assert.AreEqual(expected, heap.Pop().Value,
                            $"Heap diverged from sorted order on trial {trial}, op {op}");
                    }
                }

                // Drain whatever is left.
                reference.Sort();
                for (int i = 0; i < reference.Count; i++)
                {
                    Assert.AreEqual(reference[i], heap.Pop().Value,
                        $"Drain order wrong on trial {trial}, index {i}");
                }
            }
        }

        [Test]
        public void Pop_HandlesDuplicateValues()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            foreach (var value in new[] { 4, 4, 1, 4, 1 })
                heap.Push(new IntItem(value));

            Assert.AreEqual(1, heap.Pop().Value);
            Assert.AreEqual(1, heap.Pop().Value);
            Assert.AreEqual(4, heap.Pop().Value);
            Assert.AreEqual(4, heap.Pop().Value);
            Assert.AreEqual(4, heap.Pop().Value);
        }

        [Test]
        public void Pop_HandlesAlreadySortedInput()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            for (int i = 0; i < 20; i++) heap.Push(new IntItem(i));

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(i, heap.Pop().Value);
        }

        [Test]
        public void Pop_HandlesReverseSortedInput()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            for (int i = 19; i >= 0; i--) heap.Push(new IntItem(i));

            for (int i = 0; i < 20; i++)
                Assert.AreEqual(i, heap.Pop().Value);
        }

        [Test]
        public void Pop_HandlesNegativeValues()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            foreach (var value in new[] { 3, -7, 0, -1, 5 })
                heap.Push(new IntItem(value));

            Assert.AreEqual(-7, heap.Pop().Value);
            Assert.AreEqual(-1, heap.Pop().Value);
            Assert.AreEqual(0, heap.Pop().Value);
        }

        // ===== Peek =====

        [Test]
        public void Peek_ReturnsMinimumWithoutRemoving()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            heap.Push(new IntItem(5));
            heap.Push(new IntItem(2));

            Assert.AreEqual(2, heap.Peek().Value);
            Assert.AreEqual(2, heap.Count, "Peek must not remove anything");
            Assert.AreEqual(2, heap.Pop().Value, "Peek and Pop must agree");
        }

        // ===== Count and state =====

        [Test]
        public void Count_TracksPushesAndPops()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);

            heap.Push(new IntItem(1));
            heap.Push(new IntItem(2));
            Assert.AreEqual(2, heap.Count);
            Assert.IsFalse(heap.IsEmpty);

            heap.Pop();
            Assert.AreEqual(1, heap.Count);

            heap.Pop();
            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void Clear_EmptiesTheHeap()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            for (int i = 0; i < 10; i++) heap.Push(new IntItem(i));

            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void Reuse_AfterClear_BehavesCorrectly()
        {
            using var heap = new NativeMinHeap<IntItem>(16, Allocator.Temp);

            heap.Push(new IntItem(100));
            heap.Clear();

            foreach (var value in new[] { 3, 1, 2 })
                heap.Push(new IntItem(value));

            Assert.AreEqual(1, heap.Pop().Value,
                "A cleared heap must not retain stale elements");
        }

        [Test]
        public void GrowsBeyondInitialCapacity()
        {
            using var heap = new NativeMinHeap<IntItem>(2, Allocator.Temp);

            for (int i = 100; i > 0; i--) heap.Push(new IntItem(i));

            Assert.AreEqual(100, heap.Count);
            Assert.AreEqual(1, heap.Pop().Value,
                "Exceeding the initial capacity must not corrupt ordering");
        }

        [Test]
        public void IsCreated_ReflectsLifecycle()
        {
            var heap = new NativeMinHeap<IntItem>(4, Allocator.Temp);

            Assert.IsTrue(heap.IsCreated);

            heap.Dispose();
        }

        // ===== Error cases =====

        [Test]
        public void Pop_OnEmptyHeap_Throws()
        {
            using var heap = new NativeMinHeap<IntItem>(4, Allocator.Temp);

            Assert.Throws<InvalidOperationException>(() => heap.Pop());
        }

        [Test]
        public void Peek_OnEmptyHeap_Throws()
        {
            using var heap = new NativeMinHeap<IntItem>(4, Allocator.Temp);

            Assert.Throws<InvalidOperationException>(() => heap.Peek());
        }

        [Test]
        public void SingleElement_PushPopCycle()
        {
            using var heap = new NativeMinHeap<IntItem>(4, Allocator.Temp);

            heap.Push(new IntItem(42));

            Assert.AreEqual(42, heap.Pop().Value);
            Assert.IsTrue(heap.IsEmpty);
            Assert.Throws<InvalidOperationException>(() => heap.Pop(),
                "The heap must be genuinely empty after popping its only element");
        }

        // ===== PathfindingNode =====

        [Test]
        public void PathfindingNode_OrdersByFScore()
        {
            using var heap = new NativeMinHeap<PathfindingNode>(16, Allocator.Temp);

            heap.Push(new PathfindingNode { provinceID = 1, fScore = FixedPoint64.FromInt(10) });
            heap.Push(new PathfindingNode { provinceID = 2, fScore = FixedPoint64.FromInt(3) });
            heap.Push(new PathfindingNode { provinceID = 3, fScore = FixedPoint64.FromInt(7) });

            Assert.AreEqual(2, heap.Pop().provinceID, "Lowest fScore must come out first");
            Assert.AreEqual(3, heap.Pop().provinceID);
            Assert.AreEqual(1, heap.Pop().provinceID);
        }

        [Test]
        public void PathfindingNode_OrdersByFractionalFScore()
        {
            using var heap = new NativeMinHeap<PathfindingNode>(16, Allocator.Temp);

            heap.Push(new PathfindingNode
            {
                provinceID = 1,
                fScore = FixedPoint64.FromFraction(3, 2)
            });
            heap.Push(new PathfindingNode
            {
                provinceID = 2,
                fScore = FixedPoint64.FromFraction(1, 2)
            });

            Assert.AreEqual(2, heap.Pop().provinceID,
                "Fractional costs must order correctly, not truncate to equal integers");
        }
    }
}
