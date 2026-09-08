using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// What: the "key to binding" ledger and the "classified as added" key set that the added
/// field, method, and property families all keep. Derived catalogs supply the key of a
/// binding and add whatever else their family needs.
/// </summary>
internal abstract class AddedMemberCatalog<TBinding> where TBinding : class
{
    private readonly Dictionary<string, TBinding> _byKey = new Dictionary<string, TBinding>(StringComparer.Ordinal);
    private readonly HashSet<string> _classifiedAddedKeys = new HashSet<string>(StringComparer.Ordinal);

    public bool HasClassifiedAdded => _classifiedAddedKeys.Count > 0;

    protected IEnumerable<TBinding> RegisteredBindings => _byKey.Values;

    /// <summary>Returns the ledger key of a binding (its field, method, or property key).</summary>
    protected abstract string KeyOf(TBinding binding);

    public void Register(TBinding binding)
    {
        Debug.Assert(binding != null, "binding must not be null.");
        string key = KeyOf(binding);
        Debug.Assert(!string.IsNullOrEmpty(key), "binding key must not be null or empty.");
        _byKey[key] = binding;
        MarkClassifiedAdded(key);
    }

    public void MarkClassifiedAdded(string key)
    {
        if (key != null)
        {
            _classifiedAddedKeys.Add(key);
        }
    }

    public bool IsClassifiedAdded(string key)
    {
        return key != null && _classifiedAddedKeys.Contains(key);
    }

    public bool Contains(string key)
    {
        return key != null && _byKey.ContainsKey(key);
    }

    public TBinding FindOrNull(string key)
    {
        if (key == null)
        {
            return null;
        }

        return _byKey.TryGetValue(key, out TBinding binding) ? binding : null;
    }

    // Why the classified set survives: a key that was classified but is no longer registered is
    // how a family reports "added but unavailable".
    protected bool RemoveRegistered(string key)
    {
        return _byKey.Remove(key);
    }

    // Why indexing, not TryGetValue: callers that already hold a key from this catalog treat a
    // miss as a broken invariant rather than an absent entry.
    protected TBinding GetRegistered(string key)
    {
        return _byKey[key];
    }
}
