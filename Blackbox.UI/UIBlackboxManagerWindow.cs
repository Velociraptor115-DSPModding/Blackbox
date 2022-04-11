using System.Collections.Generic;
using DysonSphereProgram.Modding.Blackbox.UI.Builder;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  using static UIBuilderDSL;

  public class TabButton : MonoBehaviour
  {
    public TabBar TabBar;
    public ManualBehaviour TabContent;
    public Button tabButton;
    public UIButton tabUiButton;
    public int index;

    private void Awake()
    {
      if (tabUiButton == null)
        tabUiButton = gameObject.GetOrCreateComponent<UIButton>();
      if (tabButton == null)
        tabButton = gameObject.GetComponent<Button>();
      if (tabButton)
        tabButton.onClick.AddListener(OnTabBtnClick);
    }

    public void Open()
    {
      TabContent._Open();
      tabUiButton.highlighted = true;
    }

    public void Close()
    {
      tabUiButton.highlighted = false;
      TabContent._Close();
    }

    public void OnTabBtnClick()
    {
      TabBar.SelectTab(index);
    }
  }

  public class TabBar : MonoBehaviour
  {
    public readonly List<TabButton> Tabs = new();
    int selectedTabIndex;

    private void Awake()
    {
      Tabs.Clear();
      Tabs.AddRange(gameObject.GetComponentsInChildren<TabButton>());
      for (int i = 0; i < Tabs.Count; i++)
      {
        Tabs[i].TabBar = this;
        Tabs[i].index = i;
      }
      if (Tabs.Count > 0)
        Tabs[0].Open();
    }

    public void SelectTab(int index)
    {
      if (index == selectedTabIndex)
        return;
      Tabs[selectedTabIndex].Close();
      selectedTabIndex = index;
      Tabs[selectedTabIndex].Open();
    }
  }

  public class UIBlackboxManagerWindow : UIModWindowBase
  {
    UIBlackboxOverviewPanel overviewPanel;
    UIBlackboxSettingsPanel settingsPanel;
    
    public override void _OnCreate()
    {
      overviewPanel =
        gameObject
          .SelectDescendant("content-bg", "overview-panel")
          .GetOrCreateComponent<UIBlackboxOverviewPanel>();
      overviewPanel._Create();

      settingsPanel =
        gameObject
          .SelectDescendant("content-bg", "settings-panel")
          .GetOrCreateComponent<UIBlackboxSettingsPanel>();
      settingsPanel._Create();
    }

    public override void _OnDestroy()
    {
      
    }

    public override bool _OnInit()
    {
      overviewPanel._Init(null);
      settingsPanel._Init(null);
      return true;
    }

    public override void _OnFree()
    {

    }

    public override void _OnUpdate()
    {
      if (overviewPanel)
        overviewPanel._Update();
      if (settingsPanel)
        settingsPanel._Update();
    }
  }

  public class ModdedUIBlackboxManagerWindow : IModdedUI<UIBlackboxManagerWindow>
  {
    private GameObject gameObject;
    private UIBlackboxManagerWindow uiBlackboxManagerWindow;

    public UIBlackboxManagerWindow Component => uiBlackboxManagerWindow;
    public GameObject GameObject => gameObject;

    object IModdedUI.Component => uiBlackboxManagerWindow;

    public void CreateUI()
    {
      var windowsObj = UIRoot.instance.uiGame.statWindow.transform.parent;

      var managerWindowObj =
        Create.PlainWindow("Blackbox Manager Window")
          .ChildOf(windowsObj)
          .WithAnchor(Anchor.TopLeft)
          .OfSize(900, 550)
          .At(300, -180)
          .WithScrollCapture()
          .WithTitle("Blackbox Manager")
          ;

      gameObject = managerWindowObj.uiElement;

      var contentBg =
        Create.UIElement("content-bg")
          .ChildOf(managerWindowObj)
          .WithAnchor(Anchor.Stretch)
          .WithPivot(0.5f, 1f)
          .OfSize(-20, -55)
          .At(0, -45)
          .WithComponent(out Image _, new ImageProperties { raycastTarget = false, color = Color.black.AlphaMultiplied(0.7f)})
          ;

      var overviewPanelUIElement =
        Create.UIElement("overview-panel")
          .ChildOf(contentBg)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          ;

      var settingsPanelUIElement =
        Create.UIElement("settings-panel")
          .ChildOf(contentBg)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          ;
      
      InitializeOverviewPanel(overviewPanelUIElement);
      overviewPanelUIElement
        .WithComponent(out UIBlackboxOverviewPanel overviewPanel);

      InitializeSettingsPanel(settingsPanelUIElement);
      settingsPanelUIElement
        .WithComponent(out UIBlackboxSettingsPanel settingsPanel);

      var tabBar =
        Create.VerticalLayoutGroup("tab-bar")
          .ChildOf(managerWindowObj.panelBg)
          .WithAnchor(Anchor.TopLeft)
          .WithPivot(1, 1)
          .At(10, -45)
          .WithChildAlignment(TextAnchor.MiddleCenter)
          .ForceExpand(width: true, height: false)
          .ChildControls(width: true, height: true)
          .WithLayoutSize(100f, 0f)
          .WithContentSizeFitter(ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize)
          .WithComponent(out TranslucentImage _, UIBuilder.plainWindowPanelBgProperties)
          ;
      
      static UIButton.Transition CreateTabButtonTransition(Graphic target) => new()
      {
        damp = 0.3f,
        disabledColor = new Color(0.6557f, 0.9145f, 1f, 0f),
        highlightAlphaMultiplier = 1,
        highlightColorMultiplier = 1,
        highlightColorOverride = new Color(0.6549f, 0.9137f, 1f, 0.3176f),
        highlightSizeMultiplier = 1,
        mouseoverColor = new Color(0.6557f, 0.9145f, 1f, 0.1882f),
        mouseoverSize = 1,
        normalColor = Color.white.AlphaMultiplied(0f),
        pressedColor = new Color(0.6557f, 0.9145f, 1f, 0.1137f),
        pressedSize = 1,
        target = target
      };

      static UIElementContext CreateTabButton(string name, string tabText, ManualBehaviour tabContent) =>
        Create.Button(name, tabText, null)
          .AddChildren(
            Create.UIElement("col")
              .WithAnchor(Anchor.Stretch)
              .WithComponent(out Image overviewBtnImg, new ImageProperties { color = Color.white.AlphaMultiplied(0f) })
          )
          .WithTransitions(CreateTabButtonTransition(overviewBtnImg))
          .WithInteractionAudios(new UIButton.AudioSettings { downName = "ui-click-0" })
          .WithLayoutSize(50f, 55f, flexibleWidth: 1f)
          .WithComponent((TabButton x) => x.TabContent = tabContent)
          ;
      
      managerWindowObj.InitializeComponent(out uiBlackboxManagerWindow);

      tabBar
        .AddChildren(
          CreateTabButton("overview-btn", "Overview", overviewPanel),
          CreateTabButton("settings-btn", "Settings", settingsPanel)
        )
        .WithComponent(out TabBar _)
        ;
      
      uiBlackboxManagerWindow._Close();
    }
    
    static void InitializeSettingsPanel(UIElementContext root)
    {
      var scrollView =
        Create.ScrollView("scroll-view", new ScrollViewConfiguration(ScrollViewAxis.BothVerticalAndHorizontal))
          .ChildOf(root)
          .WithAnchor(Anchor.Stretch)
          .WithPivot(0, 1)
          .At(0, 0)
          ;

      var contentRoot = scrollView.scrollRect.content.gameObject;
      var viewportBg =
        Create.UIElement("bg")
          .ChildOf(scrollView.scrollRect.viewport)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          .WithComponent((Image x) => x.color = Color.clear);
    
      viewportBg.transform.SetAsFirstSibling();
      
      var mainPadding = 10;
      const int verticalSpacing = 10;
      var rootLayoutGroup = 
        Select.VerticalLayoutGroup(contentRoot)
          .WithPadding(new RectOffset(mainPadding, mainPadding, mainPadding + verticalSpacing, mainPadding + verticalSpacing))
          .WithSpacing(verticalSpacing)
          .WithChildAlignment(TextAnchor.UpperLeft)
          .ForceExpand(width: false, height: false)
          .ChildControls(width: true, height: true)
          .WithContentSizeFitter(ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize)
          ;
      
      static HorizontalLayoutGroupContext CreateEntry(VerticalLayoutGroupContext root, string configName, params RectTransform[] children)
      {
        var horizontalSpacing = 8;
      
        var currentEntry = 
            Create.HorizontalLayoutGroup("config-entry")
              .WithChildAlignment(TextAnchor.MiddleLeft)
              .WithPadding(new RectOffset(horizontalSpacing, horizontalSpacing, 0, 0))
              .WithSpacing(horizontalSpacing)
              .ForceExpand(width: false, height: false)
              .ChildControls(width: true, height: true)
              .ChildOf(root)
          // .WithContentSizeFitter(ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize)
          ;

        Create.Text("name")
          .WithLocalizer(configName)
          .WithFontSize(16)
          .WithAlignment(TextAnchor.MiddleLeft)
          .WithLayoutSize(250, 34)
          .ChildOf(currentEntry)
          ;

        currentEntry.AddChildren(children);

        return currentEntry;
      }
      
      static ToggleContext CreateOnOffToggle(IDataBindSource<bool> binding)
      {
        var buttonBlueIvyColor = new Color(0.24f, 0.6f, 0.72f);
        var buttonBlueFountainColor = new Color(0.37f, 0.72f, 0.84f);
      
        var onState = UIBuilder.buttonSelectableProperties.colors.Value with
        {
          normalColor = buttonBlueIvyColor,
          highlightedColor = buttonBlueFountainColor,
          pressedColor = buttonBlueFountainColor,
          fadeDuration = 0.05f
        };
        var offState = onState with
        {
          normalColor = Color.white.RGBMultiplied(0.55f), // 0.8235f
          highlightedColor = Color.white.RGBMultiplied(0.6f),
          pressedColor = Color.white.RGBMultiplied(0.6f) // new Color(0.5566f, 0.5566f, 0.5566f, 1f)
        };

        return
          Create.Toggle("checkbox")
            .Bind(binding)
            .WithVisuals((IProperties<Image>) new ImageProperties { sprite = UIBuilder.spriteCheckboxOff })
            .WithOnOffVisualsAndSprites(onState, offState, UIBuilder.spriteCheckboxOn, UIBuilder.spriteCheckboxOff)
            ;
      }
      
      var autoBlackbox = new DelegateDataBindSource<bool>(
        () => BlackboxManager.Instance.autoBlackbox.isActive,
        value => BlackboxManager.Instance.autoBlackbox.isActive = value
      );
      CreateEntry(rootLayoutGroup, "Auto-blackbox", CreateOnOffToggle(autoBlackbox).transform);
      
      var forceNoStacking = new DelegateDataBindSource<bool>(
        () => BlackboxBenchmark.forceNoStackingConfig,
        value => BlackboxBenchmark.forceNoStackingConfig = value
      );
      CreateEntry(rootLayoutGroup, "Force No Stacking", CreateOnOffToggle(forceNoStacking).transform);
      
      var logProfiledData = new DelegateDataBindSource<bool>(
        () => BlackboxBenchmark.logProfiledData,
        value => BlackboxBenchmark.logProfiledData = value
      );
      CreateEntry(rootLayoutGroup, "Log Profiled Data", CreateOnOffToggle(logProfiledData).transform);
      
      var continuousLogging = new DelegateDataBindSource<bool>(
        () => BlackboxBenchmark.continuousLogging,
        value => BlackboxBenchmark.continuousLogging = value
      );
      CreateEntry(rootLayoutGroup, "Continuous Logging", CreateOnOffToggle(continuousLogging).transform);
      
      var analyseInBackground = new DelegateDataBindSource<bool>(
        () => Blackbox.analyseInBackgroundConfig,
        value => Blackbox.analyseInBackgroundConfig = value
      );
      CreateEntry(rootLayoutGroup, "Analyse in background thread", CreateOnOffToggle(analyseInBackground).transform);
    }
    
    static void InitializeOverviewPanel(UIElementContext root)
    {
      var scrollView =
        Create.ScrollView("scroll-view", new ScrollViewConfiguration(ScrollViewAxis.BothVerticalAndHorizontal))
          .ChildOf(root)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          ;

      var contentRoot = scrollView.scrollRect.content.gameObject;
      var viewportBg =
        Create.UIElement("bg")
          .ChildOf(scrollView.scrollRect.viewport)
          .WithAnchor(Anchor.Stretch)
          .WithPivot(0, 1)
          .At(0, 0)
          .WithComponent((Image x) => x.color = Color.clear);
    
      viewportBg.transform.SetAsFirstSibling();

      Select.UIElement(scrollView.scrollRect.viewport)
        .WithPivot(0, 1)
        ;
      
      // var mainPadding = 0;
      // const int verticalSpacing = 0;
      var rootLayoutGroup = 
        Select.VerticalLayoutGroup(contentRoot)
          .WithAnchor(Anchor.TopStretch)
          .WithPivot(0, 1)
          .At(0, 0)
          .OfSize(0, viewportBg.transform.rect.height)
          // .WithPadding(new RectOffset(mainPadding, mainPadding, mainPadding + verticalSpacing, mainPadding + verticalSpacing))
          // .WithSpacing(verticalSpacing)
          // .WithChildAlignment(TextAnchor.UpperLeft)
          .ForceExpand(width: true, height: false)
          .ChildControls(width: true, height: true)
          // .WithContentSizeFitter(ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize)
          ;
      
      {
        var blackboxEntryPrefab =
          Create.UIElement("blackbox-entry-prefab")
            // .Deactivate()
            .ChildOf(rootLayoutGroup)
            .WithAnchor(Anchor.TopStretch)
            .WithPivot(0, 1)
            .At(0, 0)
            .OfSize(0, 120)
            .WithLayoutSize(0, 120, flexibleWidth: 1f)
            ;
        
        // var entireArea = 
        //   Create.UIElement("entire-area")
        //     .ChildOf(blackboxEntryPrefab)
        //     .WithAnchor(Anchor.Stretch)
        //     .At(0, 0)
        //     .WithComponent((Image x) => x.color = Color.green.AlphaMultiplied(0.2f))
        //     ;

        var name =
          Create.Text("name")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.TopLeft)
            .OfSize(100, 26)
            .At(30, -15)
            .WithFont(UIBuilder.fontSAIRASB)
            .WithFontSize(16)
            .WithAlignment(TextAnchor.UpperLeft)
            .WithText("Name")
            ;

        var statusLabel =
          Create.Text("status-label")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.TopLeft)
            .OfSize(100, 20)
            .At(30, -40)
            .WithMaterial(UIBuilder.materialWidgetTextAlpha5x)
            .WithFont(UIBuilder.fontSAIRASB)
            .WithFontSize(14)
            .WithAlignment(TextAnchor.UpperLeft)
            .WithColor(Color.white.AlphaMultiplied(0.5f))
            .WithText("Status")
            ;

        var progressBar =
          Create.UIElement("progress-bar")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.Center)
            .At(0, 0)
            ;

        var barGroup =
          Create.UIElement("bar-group")
            .ChildOf(progressBar)
            .WithAnchor(Anchor.Center)
            .At(0, 0)
            .OfSize(250, 8)
            ;

        var barBg =
          Create.UIElement("bar-bg")
            .ChildOf(barGroup)
            .WithAnchor(Anchor.CenterStretch)
            .At(0, 0)
            .OfSize(-2, 10)
            .WithComponent((Image x) =>
            {
              x.sprite = UIBuilder.spriteBar4px;
              x.material = UIBuilder.materialWidgetAlpha5x;
              x.color = Color.black.AlphaMultiplied(0.7f);
            })
            ;
        
        var barFg =
          Create.UIElement("bar-fg")
            .ChildOf(barGroup)
            .WithAnchor(Anchor.CenterStretch)
            .At(0, 0)
            .OfSize(-2, 10)
            .WithComponent((Image x) =>
            {
              x.sprite = UIBuilder.spriteBar4px;
              x.material = UIBuilder.materialWidgetAlpha5x;
              x.color = new Color(0.9906f, 0.5897f, 0.3691f, 0.5294f);
              x.fillMethod = Image.FillMethod.Horizontal;
              x.fillAmount = 0;
              x.type = Image.Type.Filled;
            })
            ;
        
        var barFgPoint =
          Create.UIElement("point")
            .ChildOf(barFg)
            .WithAnchor(Anchor.Left)
            .WithPivot(0.5f, 0.5f)
            .At(0, 0)
            .OfSize(6, 6)
            .WithComponent((Image x) =>
            {
              x.sprite = UIBuilder.spriteRoundBlur256;
              x.material = UIBuilder.materialWidgetAdd5x;
              x.color = new Color(0.9906f, 0.5897f, 0.3691f, 0.5383f);
            })
            ;
        
        var progressText =
          Create.Text("progress-text")
            .ChildOf(progressBar)
            .WithAnchor(Anchor.StretchRight)
            .WithPivot(0f, 0.5f)
            .At(130, 0)
            .OfSize(60, 0)
            .WithMaterial(UIBuilder.materialWidgetTextAlpha5x)
            .WithColor(new Color(0.9906f, 0.5897f, 0.3691f, 0.7255f))
            .WithFont(UIBuilder.fontSAIRASB)
            .WithFontSize(16)
            .WithAlignment(TextAnchor.MiddleRight)
            .WithText("0 / 100")
            ;

        var pauseResumeBtn =
          Create.Button("pause-resume-btn", "Pause / Resume")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.BottomLeft)
            .At(30, 20)
            .OfSize(90, 30)
            .WithVisuals((IProperties<Image>) UIBuilder.buttonImgProperties)
            .WithFontSize(14)
            ;
        
        var deleteBtn =
          Create.Button("delete-btn", "Delete")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.BottomRight)
            .At(-20, 20)
            .OfSize(90, 30)
            .WithVisuals((IProperties<Image>) UIBuilder.buttonImgProperties.WithColor(UIBuilder.buttonRedColor))
            .WithFontSize(14)
            ;
        
        var highlightBtn =
          Create.Button("highlight-btn", "Highlight")
            .ChildOf(blackboxEntryPrefab)
            .WithAnchor(Anchor.BottomRight)
            .At(-130, 20)
            .OfSize(90, 30)
            .WithVisuals((IProperties<Image>) UIBuilder.buttonImgProperties)
            .WithFontSize(14)
            ;

        Create.UIElement("sep-line-0")
          .ChildOf(blackboxEntryPrefab)
          .WithAnchor(Anchor.BottomStretch)
          .At(0, 0)
          .OfSize(-2, 2)
          .WithComponent((Image x) => x.color = new Color(0.7689f, 0.9422f, 1f, 0.0314f));
      }
    }

    public void DestroyUI()
    {
      if (uiBlackboxManagerWindow != null)
      {
        uiBlackboxManagerWindow._OnDestroy();
      }
      uiBlackboxManagerWindow = null;
      
      if (gameObject != null)
      {
        Object.Destroy(gameObject);
      }
      gameObject = null;
    }

    public void Free()
    {
      uiBlackboxManagerWindow?._OnFree();
    }

    public void Init()
    {
       
    }

    public void Update()
    {
      uiBlackboxManagerWindow?._OnUpdate();
    }
  }
}
