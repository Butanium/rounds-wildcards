using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
class ILDump {
  static Dictionary<short, OpCode> ops = new Dictionary<short, OpCode>();
  static string dir;
  static void Main(string[] a) {
    dir = a[0];
    AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
      string p = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
      return File.Exists(p) ? Assembly.LoadFrom(p) : null;
    };
    foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) { OpCode o = (OpCode)f.GetValue(null); ops[o.Value] = o; }
    Assembly asm = Assembly.LoadFrom(Path.Combine(dir, "Assembly-CSharp.dll"));
    for (int i = 1; i < a.Length; i++) {
      string[] tm = a[i].Split(new char[]{':'});
      Type t = asm.GetType(tm[0]);
      foreach (MethodBase m in AllMethods(t)) if (m.Name == tm[1]) Dump(m);
    }
  }
  static IEnumerable<MethodBase> AllMethods(Type t) {
    BindingFlags bf = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
    foreach (MethodInfo m in t.GetMethods(bf)) yield return m;
    foreach (ConstructorInfo c in t.GetConstructors(bf)) yield return c;
    foreach (Type n in t.GetNestedTypes(BindingFlags.NonPublic|BindingFlags.Public)) foreach (MethodBase m in AllMethods(n)) yield return m;
  }
  static void Dump(MethodBase m) {
    Console.WriteLine("=== " + m.DeclaringType + "::" + m);
    MethodBody b = m.GetMethodBody(); if (b == null) return;
    byte[] il = b.GetILAsByteArray(); int p = 0;
    while (p < il.Length) {
      int start = p; short v = il[p++];
      if (v == 0xFE) v = (short)(0xFE00 | il[p++]);
      OpCode o = ops[v]; string arg = "";
      switch (o.OperandType) {
        case OperandType.InlineNone: break;
        case OperandType.ShortInlineBrTarget: arg = "-> " + (p + 1 + (sbyte)il[p]); p += 1; break;
        case OperandType.ShortInlineI: case OperandType.ShortInlineVar: arg = il[p].ToString(); p += 1; break;
        case OperandType.InlineVar: arg = BitConverter.ToInt16(il, p).ToString(); p += 2; break;
        case OperandType.InlineBrTarget: arg = "-> " + (p + 4 + BitConverter.ToInt32(il, p)); p += 4; break;
        case OperandType.InlineI: arg = BitConverter.ToInt32(il, p).ToString(); p += 4; break;
        case OperandType.InlineI8: arg = BitConverter.ToInt64(il, p).ToString(); p += 8; break;
        case OperandType.ShortInlineR: arg = BitConverter.ToSingle(il, p).ToString(); p += 4; break;
        case OperandType.InlineR: arg = BitConverter.ToDouble(il, p).ToString(); p += 8; break;
        case OperandType.InlineSwitch: { int n = BitConverter.ToInt32(il, p); p += 4 + 4 * n; arg = "switch"; break; }
        case OperandType.InlineString: arg = "\"" + m.Module.ResolveString(BitConverter.ToInt32(il, p)) + "\""; p += 4; break;
        default: {
          int tok = BitConverter.ToInt32(il, p); p += 4;
          try {
            MemberInfo mi = m.Module.ResolveMember(tok, m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null, m.IsGenericMethod ? m.GetGenericArguments() : null);
            arg = (mi.DeclaringType != null ? mi.DeclaringType.Name + "::" : "") + mi.ToString();
          } catch (Exception e) { arg = "tok " + tok.ToString("X") + " " + e.GetType().Name; }
          break; }
      }
      Console.WriteLine("  " + start.ToString("D4") + " " + o.Name + " " + arg);
    }
  }
}
