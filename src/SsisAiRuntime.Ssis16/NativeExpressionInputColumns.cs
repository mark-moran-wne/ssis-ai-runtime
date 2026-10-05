using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;

namespace SsisAiRuntime.Ssis16
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class NativeExpressionInputColumns : IDTSInputColumnCollection100
    {
        private readonly IDTSInputColumnCollection100 columns;
        public NativeExpressionInputColumns(IDTSInputColumnCollection100 columns) { this.columns = columns; }
        public HashSet<int> BindingLineageIds { get; } = new HashSet<int>();
        public int GeneralReads { get; private set; }
        public int MutationAttempts { get; private set; }
        public IDTSInputColumn100 this[object index] { get { GeneralReads++; return columns[index]; } }
        public int Count => columns.Count;
        public IEnumerator GetEnumerator() { GeneralReads++; return columns.GetEnumerator(); }
        public IDTSInputColumn100 GetObjectByID(int id) { GeneralReads++; return columns.GetObjectByID(id); }
        public int GetObjectIndexByID(int id) { GeneralReads++; return columns.GetObjectIndexByID(id); }
        public IDTSInputColumn100 FindObjectByID(int id) { GeneralReads++; return columns.FindObjectByID(id); }
        public int FindObjectIndexByID(int id) { GeneralReads++; return columns.FindObjectIndexByID(id); }
        public IDTSInputColumn100 GetInputColumnByLineageID(int id) { return Record(columns.GetInputColumnByLineageID(id)); }
        public IDTSInputColumn100 GetInputColumnByName(string name, string sourceComponentName) { return Record(columns.GetInputColumnByName(name, sourceComponentName)); }
        private IDTSInputColumn100 Record(IDTSInputColumn100 column)
        {
            if (column != null && column.LineageID > 0) { BindingLineageIds.Add(column.LineageID); }
            return column;
        }
        private void Reject() { MutationAttempts++; throw new NotSupportedException("The reference collection is read-only."); }
        public IDTSInputColumn100 New() { Reject(); return null; }
        public IDTSInputColumn100 NewAt(int index) { Reject(); return null; }
        public void RemoveObjectByIndex(object index) { Reject(); }
        public void RemoveObjectByID(int id) { Reject(); }
        public void RemoveAll() { Reject(); }
        public void SetIndex(int id, int index) { Reject(); }
    }
}