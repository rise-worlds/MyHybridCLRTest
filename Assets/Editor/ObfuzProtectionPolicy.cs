using System;
using System.Collections.Generic;
using Obfuz.ObfusPasses.SymbolObfus.Policies;
using dnlib.DotNet;

public class ObfuzProtectionPolicy : ObfuscationPolicyBase
{
    // 需要保护的类名列表
    private HashSet<string> protectedTypes = new HashSet<string>
    {
        // XLua
        "RiseClient.ILuaManager",
    };
    
    // 需要保护的命名空间前缀
    private string[] protectedNamespaces = new[] 
    {
        // "XLua",
        // "Obfuz",
        "keep"
    };

    public ObfuzProtectionPolicy(object systemRenameObj) { }

    private bool IsProtectedType(TypeDef typeDef)
    {
        string fullName = typeDef.FullName;

        if (typeDef.HasGenericParameters && fullName.Contains('`'))
            fullName = fullName.Split('`')[0];

        // 检查是否在保护列表中
        if (protectedTypes.Contains(fullName))
            return true;
            
        // 检查是否在保护的命名空间中
        foreach (var ns in protectedNamespaces)
        {
            if (fullName.StartsWith(ns))
                return true;
        }
        
        return false;
    }

    public override bool NeedRename(dnlib.DotNet.TypeDef typeDef)
    {
        if (IsProtectedType(typeDef))
            return false;
        
        return true; // 其他类型可以混淆
    }

    public override bool NeedRename(dnlib.DotNet.MethodDef methodDef)
    {
        // 检查方法所属的类型是否需要保护
        if (IsProtectedType(methodDef.DeclaringType))
            return false;
        return true;
    }

    public override bool NeedRename(dnlib.DotNet.FieldDef fieldDef)
    {
        // 检查字段所属的类型是否需要保护
        if (IsProtectedType(fieldDef.DeclaringType))
            return false;
        return true;
    }

    public override bool NeedRename(dnlib.DotNet.PropertyDef propertyDef)
    {
        // 检查属性所属的类型是否需要保护
        return NeedRename(propertyDef.DeclaringType);
    }

    public override bool NeedRename(dnlib.DotNet.EventDef eventDef)
    {
        // 检查事件所属的类型是否需要保护
        return NeedRename(eventDef.DeclaringType);
    }
}