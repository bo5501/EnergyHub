using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

// SigDump <asmPath> [typeRegex] [--memberRegex <rx>] [--summary]
// 列出程序集里的类型/字段/属性/方法签名。用于核对 modding API 的真实形态。

var asmPath = Path.GetFullPath(args[0]);
string typeRx = null, memberRx = null;
bool summary = false, attrs = false, tattrs = false;

for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--memberRegex") memberRx = args[++i];
    else if (args[i] == "--summary") summary = true;
    else if (args[i] == "--attrs") attrs = true;
    else if (args[i] == "--tattrs") { tattrs = true; typeRx = args[++i]; }
    else typeRx = args[i];
}

var asm = AssemblyDefinition.ReadAssembly(asmPath, new ReaderParameters { ReadSymbols = false });
var tRx = typeRx == null ? null : new Regex(typeRx, RegexOptions.IgnoreCase);
var mRx = memberRx == null ? null : new Regex(memberRx, RegexOptions.IgnoreCase);

if (attrs)
{
    Console.OutputEncoding = Encoding.UTF8;
    foreach (var a in asm.CustomAttributes)
    {
        var argsTxt = string.Join(", ", a.ConstructorArguments.Select(x =>
            x.Value is CustomAttributeArgument[] arr
                ? "[" + string.Join(",", arr.Select(y => y.Value)) + "]"
                : x.Value?.ToString()));
        Console.WriteLine($"[{a.AttributeType.Name}] {argsTxt}");
    }
    return;
}

static string TN(TypeReference t)
{
    if (t == null) return "?";
    switch (t)
    {
        case GenericInstanceType g:
            return g.ElementType.Name + "<" + string.Join(",", g.GenericArguments.Select(TN)) + ">";
        case ArrayType a:
            return TN(a.ElementType) + "[" + new string(',', a.Rank - 1) + "]";
        case ByReferenceType r:
            return "ref " + TN(r.ElementType);
        case PointerType p:
            return TN(p.ElementType) + "*";
        case GenericParameter gp:
            return "!" + gp.Name;
        case RequiredModifierType rm:
            return TN(rm.ElementType);
        default:
            return t.Name;
    }
}

var types = new List<TypeDefinition>();
void Walk(TypeDefinition t)
{
    types.Add(t);
    foreach (var n in t.NestedTypes) Walk(n);
}
foreach (var t in asm.MainModule.Types) Walk(t);

if (tattrs)
{
    Console.OutputEncoding = Encoding.UTF8;
    foreach (var t in types)
    {
        if (!new Regex(typeRx, RegexOptions.IgnoreCase).IsMatch(t.FullName)) continue;
        Console.WriteLine("TYPE " + t.FullName);
        foreach (var a in t.CustomAttributes)
        {
            var at = string.Join(", ", a.ConstructorArguments.Select(x =>
                x.Value is CustomAttributeArgument[] arr
                    ? "[" + string.Join(",", arr.Select(y => y.Value)) + "]"
                    : x.Value?.ToString()));
            Console.WriteLine($"  [{a.AttributeType.Name}] {at}");
        }
    }
    return;
}

var sb = new StringBuilder();
foreach (var t in types.OrderBy(x => x.FullName))
{
    if (tRx != null && !tRx.IsMatch(t.FullName)) continue;

    if (summary)
    {
        sb.AppendLine($"TYPE {t.FullName}  (methods={t.Methods.Count})");
        continue;
    }

    sb.AppendLine($"=== TYPE {t.FullName} : {TN(t.BaseType)} [{string.Join(",", t.Interfaces.Select(i => TN(i.InterfaceType)))}]");
    if (t.IsEnum)
    {
        foreach (var f in t.Fields)
            if (f.HasConstant) sb.AppendLine($"  {f.Name} = {f.Constant}");
        continue;
    }
    foreach (var f in t.Fields)
    {
        if (mRx != null && !mRx.IsMatch(f.Name)) continue;
        sb.AppendLine($"  field  {(f.IsStatic ? "static " : "")}{TN(f.FieldType)} {f.Name}");
    }
    foreach (var p in t.Properties)
    {
        if (mRx != null && !mRx.IsMatch(p.Name)) continue;
        sb.AppendLine($"  prop   {TN(p.PropertyType)} {p.Name} {{{(p.GetMethod != null ? " get;" : "")}{(p.SetMethod != null ? " set;" : "")} }}");
    }
    foreach (var m in t.Methods)
    {
        if (mRx != null && !mRx.IsMatch(m.Name)) continue;
        var gen = m.HasGenericParameters ? "<" + string.Join(",", m.GenericParameters.Select(g => g.Name)) + ">" : "";
        sb.AppendLine($"  method {(m.IsStatic ? "static " : "")}{TN(m.ReturnType)} {m.Name}{gen}({string.Join(", ", m.Parameters.Select(x => TN(x.ParameterType) + " " + x.Name))})");
    }
}

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(sb.ToString());
