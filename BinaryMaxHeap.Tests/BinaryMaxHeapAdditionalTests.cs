using System;
using System.Collections.Generic;
using Heaps;
using NUnit.Framework;

namespace UnitTests_for_BMH
{
    [TestFixture]
    public class BinaryMaxHeapAdditionalTests
    {
        [Test]
        public void SingleArgConstructor_NullComparer_UsesDefaultComparer()
        {
            var heap = new BinaryMaxHeap<int>((Comparer<int>)null);

            heap.Add(2);
            heap.Add(9);
            heap.Add(4);

            Assert.That(heap.ExtractMax(), Is.EqualTo(9));
            Assert.That(heap.ExtractMax(), Is.EqualTo(4));
            Assert.That(heap.ExtractMax(), Is.EqualTo(2));
        }

        [Test]
        public void GenericHeap_WorksWithStrings()
        {
            var heap = new BinaryMaxHeap<string>();
            heap.Initialize(new[] { "apple", "zebra", "mango" });

            Assert.That(heap.ExtractMax(), Is.EqualTo("zebra"));
            Assert.That(heap.ExtractMax(), Is.EqualTo("mango"));
            Assert.That(heap.ExtractMax(), Is.EqualTo("apple"));
        }

        [Test]
        public void Initialize_NullCollection_ThrowsNullReferenceException()
        {
            var heap = new BinaryMaxHeap<int>();

            Assert.Throws<NullReferenceException>(() => heap.Initialize(null));
        }

        [Test]
        public void IndexerSet_DecreasingElementBreaksHeap_RebuildHeapFixesIt()
        {
            var heap = new BinaryMaxHeap<int>();
            heap.Initialize(new[] { 100, 50, 40, 30, 20 });

            heap[1] = 1;
            Assert.That(heap[1] < heap[3], Is.True);

            heap.RebuildHeap();
            Assert.That(heap.ToArray(), Is.EqualTo(new[] { 100, 30, 40, 1, 20 }));
        }

        [Test]
        public void Union_WithCustomComparer_IgnoresOriginalComparerAndUsesDefault()
        {
            var reversed = Comparer<int>.Create((a, b) => b.CompareTo(a));
            var caller = new BinaryMaxHeap<int>(reversed);
            var first = new BinaryMaxHeap<int>(reversed);
            first.Initialize(new[] { 1, 5 });
            var second = new BinaryMaxHeap<int>(reversed);
            second.Initialize(new[] { 2, 8 });

            var merged = caller.Union(ref first, ref second);

            Assert.That(merged.Peek(), Is.EqualTo(8));
        }
    }
}
