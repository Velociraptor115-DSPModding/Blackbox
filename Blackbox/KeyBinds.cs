using System.Collections.Generic;
using UnityEngine;
using CommonAPI.Systems;

namespace DysonSphereProgram.Modding.Blackbox
{
  public static class KeyBinds
  {
    public const string CreateBlackbox = nameof(CreateBlackbox);

    private static List<string> keyBinds = new List<string>
    {
      CreateBlackbox,
    };

    private static List<CombineKey> defaultBindings = new List<CombineKey>
    {
      new CombineKey((int)KeyCode.N, CombineKey.CTRL_COMB, ECombineKeyAction.OnceClick, false),
    };

    private static List<string> keyBindDescriptions = new List<string>
    {
      "Create Blackbox from selection",
    };

    private static int keyBindId(string keyBind) => keyBinds.IndexOf(keyBind) + 250;

    public static void RegisterKeyBinds()
    {
      foreach (var keyBind in keyBinds)
      {
        if (!CustomKeyBindSystem.HasKeyBind(keyBind))
        {
          var builtinKey = new BuiltinKey
          {
            name = keyBind,
            id = keyBindId(keyBind),
            key = defaultBindings[keyBinds.IndexOf(keyBind)],
            canOverride = true,
            conflictGroup = 4095 // I have no idea what this is, but to be on the safer side
                                 // I'm going to make it conflict with everything that isn't a mouse key
          };
          if (builtinKey.key.action == ECombineKeyAction.LongPress)
            CustomKeyBindSystem.RegisterKeyBind<HoldKeyBind>(builtinKey);
          else
            CustomKeyBindSystem.RegisterKeyBind<PressKeyBind>(builtinKey);
          ProtoRegistry.RegisterString("KEY" + keyBind, keyBindDescriptions[keyBinds.IndexOf(keyBind)]);
        }
      }
    }
  }
}