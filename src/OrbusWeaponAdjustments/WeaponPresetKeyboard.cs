using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace OrbusWeaponAdjustments;

/// <summary>
/// Opens Orbus's REAL in-world VR keyboard instead of assuming a PowerUI
/// HTML input automatically invokes it. Community OrbusKeyboard.ShowKeyboard
/// maps to Renewed/Spades lu.efy(Action&lt;string&gt;), and the local
/// PlayerCharacter.myKeyboard maps to ceh.beue.
/// </summary>
internal static class WeaponPresetKeyboard
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    private static readonly List<object> KeepAlive = new();

    private static Type? Find(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = a.GetType(name, false);
                if (type != null) return type;
            }
            catch { }
        }
        return null;
    }

    private static object? Member(object? target, string name)
    {
        if (target == null) return null;
        try
        {
            Type type = target is Type t ? t : target.GetType();
            object? instance = target is Type ? null : target;
            return type.GetProperty(name, Flags)?.GetValue(instance) ??
                   type.GetField(name, Flags)?.GetValue(instance);
        }
        catch { return null; }
    }

    internal static bool Open(string existing, Action<string> finished, out string reason)
    {
        reason = "Orbus VR keyboard unavailable";
        try
        {
            bool renewed = Find("ceh") != null;
            Type? character = Find(renewed ? "ceh" : "MagicalActual.PlayerCharacter");
            if (character == null) { reason = "Local player class unavailable"; return false; }

            object? player = Member(character, renewed ? "blfr" : "Instance");
            if (player == null)
            {
                string getter = renewed ? "led" : "get_Instance";
                player = character.GetMethod(getter, Flags, null, Type.EmptyTypes, null)?
                    .Invoke(null, null);
            }
            object? keyboard = Member(player, renewed ? "beue" : "myKeyboard");
            if (keyboard == null) { reason = "Local player's Orbus keyboard is not initialized"; return false; }

            string methodName = renewed ? "efy" : "ShowKeyboard";
            MethodInfo? show = keyboard.GetType().GetMethods(Flags).FirstOrDefault(m =>
                m.Name == methodName && m.GetParameters().Length == 1);
            if (show == null) { reason = "Native VR keyboard ShowKeyboard method unavailable"; return false; }

            Type callbackType = show.GetParameters()[0].ParameterType;
            MethodInfo? invoke = callbackType.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(void) ||
                invoke.GetParameters().Length != 1)
            {
                reason = "Game keyboard's callback could not be recognized";
                return false;
            }
            Type valueType = invoke.GetParameters()[0].ParameterType;
            // IL2CPP interop can expose the string as System.String OR an
            // Il2CppSystem.String wrapper. The old exact typeof(string)
            // check rejected the native callback before even opening it.
            if (valueType != typeof(string) &&
                valueType.FullName != "Il2CppSystem.String")
            {
                reason = "Game keyboard is using an unsupported text type";
                return false;
            }

            object? converted = null;
            ParameterExpression parameter = Expression.Parameter(valueType, "typedName");
            MethodInfo objectToString = typeof(object).GetMethod("ToString")!;
            Delegate managed = Expression.Lambda(
                typeof(Action<>).MakeGenericType(valueType),
                Expression.Call(
                    Expression.Constant(finished),
                    typeof(Action<string>).GetMethod("Invoke")!,
                    Expression.Call(Expression.Convert(parameter, typeof(object)),
                        objectToString)),
                parameter).Compile();

            if (callbackType.IsInstanceOfType(managed))
                converted = managed;
            else
            {
                foreach (MethodInfo convert in typeof(DelegateSupport)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "ConvertDelegate" &&
                                m.IsGenericMethodDefinition && m.GetParameters().Length == 1))
                {
                    try
                    {
                        object? result = convert.MakeGenericMethod(callbackType)
                            .Invoke(null, new object[] { managed });
                        if (result != null && callbackType.IsInstanceOfType(result))
                        {
                            converted = result;
                            break;
                        }
                    }
                    catch { /* Try the next available interop converter. */ }
                }
            }
            if (converted == null) { reason = "Could not bind the IL2CPP keyboard callback"; return false; }

            // Set the native keyboard's existing text to support Rename.
            string textFieldName = renewed ? "ruh" : "currentText";
            FieldInfo? textField = keyboard.GetType().GetField(textFieldName, Flags);
            if (textField?.FieldType == typeof(string))
                textField.SetValue(keyboard, existing);

            // Keep both delegates alive until the UI closes (and beyond, to
            // avoid collection of callback memory while native code holds it).
            KeepAlive.Add(managed);
            KeepAlive.Add(converted);
            show.Invoke(keyboard, new[] { converted });
            Plugin.ModLog.LogInfo("[WeaponAdjustments/Keyboard] Requested native Orbus keyboard.");
            reason = "";
            return true;
        }
        catch (Exception ex)
        {
            reason = "VR keyboard failed (" + (ex.InnerException ?? ex).GetType().Name + ")";
            return false;
        }
    }
}
