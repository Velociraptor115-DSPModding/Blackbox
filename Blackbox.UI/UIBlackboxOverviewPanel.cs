using System.Linq;
using System.Collections.Generic;
using DysonSphereProgram.Modding.Blackbox.UI.Builder;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace DysonSphereProgram.Modding.Blackbox.UI
{ 
  public class UIBlackboxOverviewPanel: ManualBehaviour
  {
    RectTransform scrollContentRect;
    RectTransform scrollViewportRect;
    RectTransform scrollVbarRect;
    UIBlackboxEntry blackboxEntry;

    int entriesLen;
    UIBlackboxEntry[] entries;
    
    public override void _OnCreate()
    {
      scrollContentRect =
        gameObject
          .SelectDescendant("scroll-view", "viewport", "content")
          ?.GetComponent<RectTransform>()
          ;

      scrollViewportRect =
        gameObject
          .SelectDescendant("scroll-view", "viewport")
          ?.GetComponent<RectTransform>()
          ;

      scrollVbarRect =
        gameObject
          .SelectDescendant("scroll-view", "v-bar")
          ?.GetComponent<RectTransform>()
          ;

      blackboxEntry =
        gameObject
          .SelectDescendant("scroll-view", "viewport", "content", "blackbox-entry-prefab")
          .GetOrCreateComponent<UIBlackboxEntry>()
          ;
      blackboxEntry._Create();

      entriesLen = Mathf.CeilToInt(scrollContentRect.rect.height / blackboxEntry.rectTransform.rect.height);
      entries = new UIBlackboxEntry[entriesLen];

      for (int i = 0; i < entriesLen; i++)
      {
        var entry = Object.Instantiate<UIBlackboxEntry>(blackboxEntry, blackboxEntry.transform.parent);
        entry._Create();
        entries[i] = entry;
      }
    }

    public override void _OnDestroy()
    {
      
    }

    public override bool _OnInit()
    {
      for (int i = 0; i < entriesLen; i++)
      {
        entries[i]._Init(null);
      }
      return true;
    }

    public override void _OnFree()
    {

    }

    public override void _OnUpdate()
    {
      var blackboxCount = BlackboxManager.Instance.blackboxes.Count;
      var displayCount = (blackboxCount > entriesLen) ? entriesLen : blackboxCount;
      var height = blackboxEntry.rectTransform.rect.height;
      if (height * blackboxCount < scrollContentRect.sizeDelta.y - 1f)
      {
        float yOffset = (blackboxCount - displayCount + 1) * height;
        yOffset = (yOffset < 0f) ? 0f : yOffset;
        if (scrollContentRect.anchoredPosition.y > yOffset)
          scrollContentRect.anchoredPosition = new Vector2(scrollContentRect.anchoredPosition.x, yOffset);
      }
      scrollContentRect.sizeDelta = new Vector2(scrollContentRect.sizeDelta.x, height * blackboxCount);
      scrollContentRect.anchoredPosition = new Vector2(Mathf.Round(scrollContentRect.anchoredPosition.x), Mathf.Round(scrollContentRect.anchoredPosition.y));
      scrollViewportRect.sizeDelta = (scrollVbarRect.gameObject.activeSelf ? new Vector2(-scrollVbarRect.rect.width, 0f) : Vector2.zero);
      int startIdx = (blackboxCount == 0) ? -1 : ((int)scrollContentRect.anchoredPosition.y / (int)height);
      int endIdx = startIdx + displayCount - 1;
      for (int i = 0; i < entriesLen; i++)
      {
        var uiBlackboxEntry = entries[i];
        int blackboxDisplayIdx = startIdx + i;
        if (uiBlackboxEntry.index != blackboxDisplayIdx)
        {
          uiBlackboxEntry.index = blackboxDisplayIdx;
          uiBlackboxEntry.SetTrans();
        }
        if (blackboxDisplayIdx >= 0 && blackboxDisplayIdx <= endIdx && blackboxDisplayIdx < BlackboxManager.Instance.blackboxes.Count)
        {
          uiBlackboxEntry.entryData = BlackboxManager.Instance.blackboxes[blackboxDisplayIdx];
          uiBlackboxEntry._Open();
        }
        else
        {
          uiBlackboxEntry._Close();
          uiBlackboxEntry.entryData = null;
        }

        uiBlackboxEntry._Update();
      }
    }
  }
}
