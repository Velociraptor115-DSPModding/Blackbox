using System.Collections.Generic;
using UnityEngine;
using CommonAPI.Systems;
using CommonAPI.Systems.ModLocalization;

namespace DysonSphereProgram.Modding.Blackbox
{
  public record KeyBind(string Id, string Description, CombineKey DefaultBinding, int ConflictGroup)
  {
    public bool IsActive => CustomKeyBindSystem.GetKeyBind(Id)?.keyValue ?? false;
  }

  public static class KeyBinds
  {
    public static readonly KeyBind CreateBlackbox = new(
        nameof(CreateBlackbox)
      , "Create Blackbox from selection"
      , new CombineKey((int)KeyCode.N, CombineKey.CTRL_COMB, ECombineKeyAction.OnceClick, false)
      , 4095
    );

    public static readonly KeyBind BlackboxManagerWindow = new(
        nameof(BlackboxManagerWindow)
      , "Blackbox Manager Window"
      , new CombineKey((int)KeyCode.M, CombineKey.CTRL_COMB, ECombineKeyAction.OnceClick, false)
      , 4095
    );

    private static readonly KeyBind[] keyBinds = new KeyBind[]
    {
        CreateBlackbox
      , BlackboxManagerWindow
    };

    public static void RegisterKeyBinds()
    {
      foreach (var keyBind in keyBinds)
      {
        if (!CustomKeyBindSystem.HasKeyBind(keyBind.Id))
        {
          var builtinKey = new BuiltinKey
          {
            name = keyBind.Id,
            id = 0,
            key = keyBind.DefaultBinding,
            canOverride = true,
            conflictGroup = keyBind.ConflictGroup
          };
          if (builtinKey.key.action == ECombineKeyAction.LongPress)
            CustomKeyBindSystem.RegisterKeyBind<HoldKeyBind>(builtinKey);
          else
            CustomKeyBindSystem.RegisterKeyBind<PressKeyBind>(builtinKey);
          LocalizationModule.RegisterTranslation("KEY" + keyBind.Id, keyBind.Description);
        }
      }
    }
  }
}