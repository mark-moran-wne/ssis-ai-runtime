using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SsisAiRuntime.Inspectors
{
    public sealed class InspectionResult<TItem> where TItem : class
    {
        public InspectionResult(IEnumerable<TItem> items, IEnumerable<UnsupportedItem> unsupportedItems)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (unsupportedItems == null)
            {
                throw new ArgumentNullException(nameof(unsupportedItems));
            }

            var itemList = new List<TItem>();
            foreach (var item in items)
            {
                itemList.Add(item ?? throw new ArgumentException("Inspection results cannot contain null items.", nameof(items)));
            }

            var unsupportedList = new List<UnsupportedItem>();
            foreach (var item in unsupportedItems)
            {
                unsupportedList.Add(item ?? throw new ArgumentException("Unsupported items cannot contain null entries.", nameof(unsupportedItems)));
            }

            Items = new ReadOnlyCollection<TItem>(itemList);
            UnsupportedItems = new ReadOnlyCollection<UnsupportedItem>(unsupportedList);
            IsComplete = unsupportedList.Count == 0;
        }

        public IReadOnlyList<TItem> Items { get; }

        public IReadOnlyList<UnsupportedItem> UnsupportedItems { get; }

        public bool IsComplete { get; }

        public static InspectionResult<TItem> Complete(IEnumerable<TItem> items)
        {
            return new InspectionResult<TItem>(items, Array.Empty<UnsupportedItem>());
        }
    }
}