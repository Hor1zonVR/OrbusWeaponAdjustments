using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Native PowerUI event callbacks which retain the actual MouseEvent.
/// Unlike generic click handlers, sliders need the real event offsetX.
/// Only binds supported Dom.Event / MouseEvent / UIEvent / PointerEvent delegates.
/// </summary>
internal static class WeaponPointerEvents
{
    private static readonly List<object> KeepAlive = new();
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

    internal static bool Bind(object element, string eventName,
        Action<object> callback, out string failure)
    {
        failure = "No compatible PowerUI pointer-event listener";
        var methods = element.GetType().GetMethods(Flags)
            .Where(m => m.Name == "addEventListener" &&
                (m.GetParameters().Length == 2 || m.GetParameters().Length == 3) &&
                m.GetParameters()[0].ParameterType == typeof(string))
            .OrderBy(m => m.GetParameters().Length)
            .ToArray();

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Type delegateType = parameters[1].ParameterType;
            MethodInfo? invoke = delegateType.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(void)) continue;
            ParameterInfo[] eventParameters = invoke.GetParameters();
            if (eventParameters.Length != 1) continue;
            Type eventType = eventParameters[0].ParameterType;
            string typeName = eventType.FullName ?? "";
            if (typeName != "Dom.MouseEvent" && typeName != "Dom.UIEvent" &&
                typeName != "Dom.PointerEvent" && typeName != "Dom.Event")
                continue;

            try
            {
                ParameterExpression parameter = Expression.Parameter(eventType, "evt");
                Delegate managed = Expression.Lambda(
                    typeof(Action<>).MakeGenericType(eventType),
                    Expression.Call(Expression.Constant(callback),
                        typeof(Action<object>).GetMethod("Invoke")!,
                        Expression.Convert(parameter, typeof(object))),
                    parameter).Compile();

                object? native = null;
                foreach (MethodInfo convert in typeof(DelegateSupport)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "ConvertDelegate" &&
                                m.IsGenericMethodDefinition &&
                                m.GetParameters().Length == 1))
                {
                    try
                    {
                        object? result = convert.MakeGenericMethod(delegateType)
                            .Invoke(null, new object[] { managed });
                        if (result != null && delegateType.IsInstanceOfType(result))
                        {
                            native = result;
                            break;
                        }
                    }
                    catch { }
                }
                if (native == null) continue;
                object[] args = parameters.Length == 3
                    ? new object[] { eventName, native, false }
                    : new object[] { eventName, native };
                method.Invoke(element, args);
                KeepAlive.Add(managed);
                KeepAlive.Add(native);
                KeepAlive.Add(callback);
                failure = "";
                return true;
            }
            catch (Exception ex)
            {
                failure = (ex.InnerException ?? ex).GetType().Name;
            }
        }

        return false;
    }
}
