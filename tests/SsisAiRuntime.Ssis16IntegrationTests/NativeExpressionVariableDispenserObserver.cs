using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using RuntimeWrapper = Microsoft.SqlServer.Dts.Runtime.Wrapper;

namespace SsisAiRuntime.Ssis16IntegrationTests
{
    public sealed class NativeExpressionScope
    {
        public NativeExpressionScope(string type, string id, string name)
        {
            Type = type;
            Id = id;
            Name = name;
        }

        public string Type { get; }
        public string Id { get; }
        public string Name { get; }
    }

    public sealed class NativeExpressionVariableBinding
    {
        public NativeExpressionVariableBinding(string namespaceName, string name, string qualifiedName,
            string scopeType, string scopeId, string scopeName)
        {
            NamespaceName = namespaceName;
            Name = name;
            QualifiedName = qualifiedName;
            ScopeType = scopeType;
            ScopeId = scopeId;
            ScopeName = scopeName;
        }

        public string NativeId => string.Empty;
        public string NamespaceName { get; }
        public string Name { get; }
        public string QualifiedName { get; }
        public string ScopeType { get; }
        public string ScopeId { get; }
        public string ScopeName { get; }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class NativeExpressionVariableDispenserObserver : RuntimeWrapper.IDTSVariableDispenser100
    {
        private readonly RuntimeWrapper.IDTSVariableDispenser100 inner;
        private readonly Dictionary<string, NativeExpressionScope> scopes;
        private readonly Dictionary<string, NativeExpressionVariableBinding> bindings = new Dictionary<string, NativeExpressionVariableBinding>(StringComparer.Ordinal);

        public NativeExpressionVariableDispenserObserver(RuntimeWrapper.IDTSVariableDispenser100 inner,
            IEnumerable<NativeExpressionScope> scopes)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.scopes = (scopes ?? throw new ArgumentNullException(nameof(scopes)))
                .ToDictionary(scope => scope.Id, StringComparer.Ordinal);
        }

        public List<string> ReadLockRequests { get; } = new List<string>();
        public int WriteAttempts { get; private set; }
        public int MutationAttempts { get; private set; }
        public int ValueReadAttempts { get; private set; }
        public IReadOnlyList<NativeExpressionVariableBinding> Bindings => bindings.Values.ToArray();

        public void ResetObservations()
        {
            ReadLockRequests.Clear();
            bindings.Clear();
            WriteAttempts = 0;
            MutationAttempts = 0;
            ValueReadAttempts = 0;
        }

        public void LockForRead(string variableName)
        {
            ReadLockRequests.Add(variableName ?? string.Empty);
            inner.LockForRead(variableName);
        }

        public void LockForWrite(string variableName)
        {
            WriteAttempts++;
            Reject("Variable write locks are refused by the native reference probe.");
        }

        public void GetVariables(out RuntimeWrapper.IDTSVariables100 variables)
        {
            inner.GetVariables(out var values);
            variables = new NativeExpressionVariablesObserver(values, this);
        }

        public void Reset() => inner.Reset();

        public void LockOneForRead(string variableName, ref RuntimeWrapper.IDTSVariables100 variables)
        {
            ReadLockRequests.Add(variableName ?? string.Empty);
            inner.LockOneForRead(variableName, ref variables);
            var values = variables;
            variables = new NativeExpressionVariablesObserver(values, this);
        }

        public void LockOneForWrite(string variableName, ref RuntimeWrapper.IDTSVariables100 variables)
        {
            WriteAttempts++;
            variables = null;
            Reject("Variable write locks are refused by the native reference probe.");
        }

        public bool Contains(string variableName) => inner.Contains(variableName);
        public string GetQualifiedName(string variableName) => inner.GetQualifiedName(variableName);

        internal RuntimeWrapper.IDTSVariable100 Record(RuntimeWrapper.IDTSVariable100 variable)
        {
            if (variable == null) { return null; }
            var namespaceName = variable.Namespace ?? string.Empty;
            var qualifiedName = variable.QualifiedName ?? string.Empty;
            var name = qualifiedName.StartsWith(namespaceName + "::", StringComparison.Ordinal)
                ? qualifiedName.Substring(namespaceName.Length + 2)
                : qualifiedName;
            var parent = variable.Parent as RuntimeWrapper.IDTSName100;
            var scopeId = parent == null ? string.Empty : parent.ID ?? string.Empty;
            var scope = scopes.TryGetValue(scopeId, out var knownScope)
                ? knownScope
                : new NativeExpressionScope("Unknown", scopeId, parent == null ? string.Empty : parent.Name ?? string.Empty);
            var binding = new NativeExpressionVariableBinding(namespaceName, name, qualifiedName,
                scope.Type, scope.Id, scope.Name);
            bindings[scopeId + "\u001f" + qualifiedName] = binding;
            return new NativeExpressionVariableObserver(variable, this);
        }

        internal RuntimeWrapper.IDTSVariable100 RefuseValueRead()
        {
            ValueReadAttempts++;
            throw new NotSupportedException("Variable values are not available to the native reference probe.");
        }

        internal void RefuseMutation()
        {
            MutationAttempts++;
            Reject("Variable collection mutations are refused by the native reference probe.");
        }

        private static void Reject(string message) => throw new NotSupportedException(message);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class NativeExpressionVariablesObserver : RuntimeWrapper.IDTSVariables100
    {
        private readonly RuntimeWrapper.IDTSVariables100 inner;
        private readonly NativeExpressionVariableDispenserObserver observer;

        public NativeExpressionVariablesObserver(RuntimeWrapper.IDTSVariables100 inner,
            NativeExpressionVariableDispenserObserver observer)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public RuntimeWrapper.IDTSVariable100 this[object index] => observer.Record(inner[index]);
        public int Count => inner.Count;
        public IEnumerator GetEnumerator() => new NativeExpressionVariableEnumerator(inner.GetEnumerator(), observer);
        public RuntimeWrapper.IDTSVariable100 Add(string name, bool readOnly, string namespaceName, object value)
        {
            observer.RefuseMutation();
            return null;
        }
        public void Remove(object index) => observer.RefuseMutation();
        public void Unlock() => inner.Unlock();
        public bool Locked => inner.Locked;
        public bool Contains(object index) => inner.Contains(index);
        public void Join(RuntimeWrapper.IDTSVariable100 variable) => observer.RefuseMutation();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class NativeExpressionVariableObserver : RuntimeWrapper.IDTSVariable100
    {
        private readonly RuntimeWrapper.IDTSVariable100 inner;
        private readonly NativeExpressionVariableDispenserObserver observer;

        public NativeExpressionVariableObserver(RuntimeWrapper.IDTSVariable100 inner,
            NativeExpressionVariableDispenserObserver observer)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public bool ReadOnly { get => inner.ReadOnly; set => observer.RefuseMutation(); }
        public string Namespace { get => inner.Namespace; set => observer.RefuseMutation(); }
        public object Value { get => observer.RefuseValueRead(); set => observer.RefuseMutation(); }
        public int DataType { get => inner.DataType; set => observer.RefuseMutation(); }
        public string QualifiedName => inner.QualifiedName;
        public bool RaiseChangedEvent { get => inner.RaiseChangedEvent; set => observer.RefuseMutation(); }
        public bool EvaluateAsExpression { get => inner.EvaluateAsExpression; set => observer.RefuseMutation(); }
        public bool SystemVariable => inner.SystemVariable;
        public bool IncludeInDebugDump { get => inner.IncludeInDebugDump; set => observer.RefuseMutation(); }
        public RuntimeWrapper.IDTSContainer100 Parent => inner.Parent;
        public object GetValueWithContext(RuntimeWrapper.IDTSEvaluatorContext100 context) => observer.RefuseValueRead();
    }

    internal sealed class NativeExpressionVariableEnumerator : IEnumerator
    {
        private readonly IEnumerator inner;
        private readonly NativeExpressionVariableDispenserObserver observer;

        public NativeExpressionVariableEnumerator(IEnumerator inner, NativeExpressionVariableDispenserObserver observer)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public object Current => observer.Record((RuntimeWrapper.IDTSVariable100)inner.Current);
        public bool MoveNext() => inner.MoveNext();
        public void Reset() => inner.Reset();
    }
}