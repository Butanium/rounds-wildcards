using System;
using System.Reflection;
using UnboundLib.Cards;
using UnityEngine;

namespace WildCards
{
    /// Registers every concrete WildCard subclass in this assembly automatically,
    /// and calls an optional `public static void Init()` on each card class (hook subscriptions, RPC setup...).
    public static class CardRegistry
    {
        public static void RegisterAll()
        {
            WildState.Register();

            MethodInfo build = null;
            foreach (MethodInfo m in typeof(CustomCard).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "BuildCard" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { build = m; break; }

            Type[] types = typeof(CardRegistry).Assembly.GetTypes();
            Array.Sort(types, delegate (Type a, Type b) { return string.CompareOrdinal(a.FullName, b.FullName); });
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(WildCard).IsAssignableFrom(t)) continue;
                try
                {
                    MethodInfo init = t.GetMethod("Init", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (init != null) init.Invoke(null, null);
                    build.MakeGenericMethod(t).Invoke(null, null);
                }
                catch (Exception e) { Debug.LogError("[WildCards] failed to register " + t.Name + ": " + e); }
            }
        }
    }
}
