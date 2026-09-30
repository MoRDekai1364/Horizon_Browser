using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.Stealth.Services;

public static class CompatJson
{
    private static readonly JsonSerializerOptions LeafOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly JsonDocumentOptions DocOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static T? Deserialize<T>(string json, Func<T>? factory = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, DocOptions); }
        catch { return null; }

        using (doc)
        {
            object? value = ReadValue(doc.RootElement, typeof(T), factory == null ? null : () => factory());
            if (value is not T typed) return null;
            Normalize(typed, 0);
            return typed;
        }
    }

    private static object? ReadValue(JsonElement el, Type type, Func<object>? rootFactory = null)
    {
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined) return null;

        Type? underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null) type = underlying;

        if (IsStringDictionary(type, out Type? dictValueType))
        {
            if (el.ValueKind != JsonValueKind.Object) throw new JsonException("Expected object.");
            var dict = (IDictionary)Activator.CreateInstance(type)!;
            foreach (var prop in el.EnumerateObject())
            {
                try
                {
                    object? v = ReadValue(prop.Value, dictValueType!);
                    if (v == null)
                    {
                        if (dictValueType == typeof(string)) v = "";
                        else continue;
                    }
                    dict[prop.Name] = v;
                }
                catch { }
            }
            return dict;
        }

        if (IsList(type, out Type? elementType))
        {
            if (el.ValueKind != JsonValueKind.Array) throw new JsonException("Expected array.");
            var list = (IList)Activator.CreateInstance(type)!;
            foreach (var item in el.EnumerateArray())
            {
                try
                {
                    object? v = ReadValue(item, elementType!);
                    if (v != null) list.Add(v);
                }
                catch { }
            }
            return list;
        }

        if (IsPlainClass(type))
        {
            if (el.ValueKind != JsonValueKind.Object) throw new JsonException("Expected object.");
            object obj = rootFactory != null ? rootFactory() : Activator.CreateInstance(type)!;
            var props = GetWritableProperties(type);
            PropertyInfo? extension = props.Values
                .FirstOrDefault(p => p.GetCustomAttribute<JsonExtensionDataAttribute>() != null);
            Dictionary<string, JsonElement>? extras = null;

            foreach (var jp in el.EnumerateObject())
            {
                if (props.TryGetValue(jp.Name, out PropertyInfo? pi) && pi != extension)
                {
                    try
                    {
                        object? v = ReadValue(jp.Value, pi.PropertyType);
                        if (v != null || AllowsNull(pi))
                            pi.SetValue(obj, v);
                    }
                    catch { }
                }
                else if (extension != null)
                {
                    extras ??= new Dictionary<string, JsonElement>();
                    extras[jp.Name] = jp.Value.Clone();
                }
            }

            if (extension != null && extras != null && extension.PropertyType.IsAssignableFrom(typeof(Dictionary<string, JsonElement>)))
                extension.SetValue(obj, extras);

            return obj;
        }

        return el.Deserialize(type, LeafOptions);
    }

    private static Dictionary<string, PropertyInfo> GetWritableProperties(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!pi.CanRead || !pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
            if (pi.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
            string name = pi.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? pi.Name;
            map[name] = pi;
        }
        return map;
    }

    private static bool AllowsNull(PropertyInfo pi)
    {
        if (Nullable.GetUnderlyingType(pi.PropertyType) != null) return true;
        if (pi.PropertyType.IsValueType) return false;
        return new NullabilityInfoContext().Create(pi).WriteState == NullabilityState.Nullable;
    }

    private static bool IsStringDictionary(Type type, out Type? valueType)
    {
        valueType = null;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return false;
        var args = type.GetGenericArguments();
        if (args[0] != typeof(string)) return false;
        valueType = args[1];
        return true;
    }

    private static bool IsList(Type type, out Type? elementType)
    {
        elementType = null;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(List<>)) return false;
        elementType = type.GetGenericArguments()[0];
        return true;
    }

    private static bool IsPlainClass(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type == typeof(string) || type.IsArray) return false;
        if (typeof(IEnumerable).IsAssignableFrom(type)) return false;
        if (type.Assembly != typeof(CompatJson).Assembly) return false;
        return type.GetConstructor(Type.EmptyTypes) != null;
    }

    private static void Normalize(object? obj, int depth)
    {
        if (obj == null || depth > 24) return;
        Type type = obj.GetType();

        if (obj is IDictionary dict)
        {
            foreach (var key in dict.Keys.Cast<object>().ToList())
            {
                object? v = dict[key];
                if (v == null) dict[key] = type.IsGenericType && type.GetGenericArguments()[1] == typeof(string) ? "" : v;
                else Normalize(v, depth + 1);
            }
            return;
        }

        if (obj is IList list && !type.IsArray)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null) list.RemoveAt(i);
            foreach (var item in list) Normalize(item, depth + 1);
            return;
        }

        if (!IsPlainClass(type)) return;

        foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!pi.CanRead || !pi.CanWrite || pi.GetIndexParameters().Length > 0) continue;
            if (pi.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;

            object? value = pi.GetValue(obj);

            bool needsDefault = value == null || (value is double d && !double.IsFinite(d)) || (value is float f && !float.IsFinite(f));
            if (needsDefault)
            {
                object fresh = Activator.CreateInstance(type)!;
                object? defaultValue = pi.GetValue(fresh);
                if (defaultValue != null)
                {
                    pi.SetValue(obj, defaultValue);
                    value = defaultValue;
                }
            }

            if (value != null && !(value is string) && !pi.PropertyType.IsValueType)
                Normalize(value, depth + 1);
        }
    }
}