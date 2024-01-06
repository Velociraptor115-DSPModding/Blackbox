using System.Text;
using DysonSphereProgram.Modding.Blackbox.UI.Builder;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  using static UIBuilderDSL;
  
  public class UIBlackboxInspectWindow: UIModWindowBase
  {
    private Blackbox blackbox;

    private Localizer titleLocalizer;
    private Text statusText;
    private Text pauseResumeBtnText;
    private Text recipeText;

    private Button pauseResumeBtn;
    private Image progressImg;
    private GameObject progressIndicator;

    private static Color errorColor = new Color(1f, 0.27f, 0.1934f, 0.7333f);
    private static Color warningColor = new Color(0.9906f, 0.5897f, 0.3691f, 0.7059f);
    private static Color okColor = new Color(0.3821f, 0.8455f, 1f, 0.7059f);
    private static Color idleColor = new Color(0.5882f, 0.5882f, 0.5882f, 0.8196f);

    private StringBuilder recipeSB = new();
    private StringBuilder powerSB = new("         W", 12);

    public override void _OnCreate()
    {
      titleLocalizer =
        gameObject
          .SelectDescendant("panel-bg", "title-text")
          ?.GetComponent<Localizer>()
          ;

      if (titleLocalizer != null)
        titleLocalizer.stringKey = "<Select Blackbox to Inspect>";

      progressIndicator =
        gameObject
          .SelectChild("progress-circle")
          ;

      progressIndicator.SetActive(false);

      progressImg =
        progressIndicator
          .SelectDescendant("circle-back", "circle-fg")
          ?.GetComponent<Image>()
          ;

      if (progressImg != null)
        progressImg.fillAmount = 0;

      statusText =
        gameObject
          .SelectDescendant("status-label")
          .GetComponent<Text>()
          ;

      pauseResumeBtn =
        gameObject
          .SelectDescendant("pause-resume-btn")
          .GetComponent<Button>()
          ;

      pauseResumeBtnText =
        pauseResumeBtn
          .gameObject
          .SelectChild("text")
          .GetComponent<Text>()
          ;

      recipeText =
        gameObject
          .SelectDescendant("recipe-box", "scroll-view", "viewport", "content", "recipe-text")
          .GetComponent<Text>()
          ;
    }

    public override void _OnDestroy()
    {
      titleLocalizer = null;
      progressImg = null;
      progressIndicator = null;
      statusText = null;
      pauseResumeBtnText = null;
      pauseResumeBtn = null;
    }

    public override bool _OnInit()
    {
      blackbox = data as Blackbox;
      if (blackbox == null)
        return false;

      if (titleLocalizer != null)
        titleLocalizer.stringKey = blackbox.Name;

      progressIndicator.SetActive(true);

      pauseResumeBtn.onClick.AddListener(OnPauseResumeBtnClick);

      Debug.Log("Setting produce to Active");

      if (blackbox.Recipe != null)
      {
        var recipe = blackbox.Recipe;
        if (!blackbox.FactoryRef.TryGetTarget(out var planetFactory))
        {
          Plugin.Log.LogError("PlanetFactory instance pulled out from under " + nameof(UIBlackboxInspectWindow) + " in " + nameof(_OnInit));
          return false;
        }
        
        recipeSB.Clear();
        recipeSB.AppendLine("Consumes:");
        foreach (var item in recipe.consumes)
        {
          var itemName = LDB.ItemName(item.Key);
          recipeSB.Append("- ");
          recipeSB.Append(item.Value);
          recipeSB.Append(" ");
          recipeSB.AppendLine(itemName);
        }
        recipeSB.AppendLine();
        
        recipeSB.AppendLine("Produces:");
        foreach (var item in recipe.produces)
        {
          var itemName = LDB.ItemName(item.Key);
          recipeSB.Append("- ");
          recipeSB.Append(item.Value);
          recipeSB.Append(" ");
          recipeSB.AppendLine(itemName);
        }
        recipeSB.AppendLine();
        
        recipeSB.AppendLine("Inputs:");
        foreach (var stationIdx in recipe.inputs)
        {
          var stationId = blackbox.Selection.stationIds[stationIdx.Key];
          var station = planetFactory.transport.stationPool[stationId];
          var stationName =
            station.isStellar ? "星际站点号".Translate() + station.gid : "本地站点号".Translate() + station.id;
          Plugin.Log.LogDebug(stationIdx.Key);
          recipeSB.Append("- ");
          recipeSB.AppendLine(stationName);
          foreach (var itemId in stationIdx.Value)
          {
            var itemName = LDB.ItemName(itemId.Key);
            recipeSB.Append("   -- ");
            recipeSB.Append(itemId.Value);
            recipeSB.Append(" ");
            recipeSB.AppendLine(itemName);
          }
        }
        recipeSB.AppendLine();
        
        recipeSB.AppendLine("Outputs:");
        foreach (var stationIdx in recipe.outputs)
        {
          var stationId = blackbox.Selection.stationIds[stationIdx.Key];
          var station = planetFactory.transport.stationPool[stationId];
          var stationName = 
            station.isStellar ? "星际站点号".Translate() + station.gid : "本地站点号".Translate() + station.id;
          recipeSB.Append("- ");
          recipeSB.AppendLine(stationName);
          foreach (var itemId in stationIdx.Value)
          {
            var itemName = LDB.ItemName(itemId.Key);
            recipeSB.Append("   -- ");
            recipeSB.Append(itemId.Value);
            recipeSB.Append(" ");
            recipeSB.AppendLine(itemName);
          }
        }
        recipeSB.AppendLine();
        
        recipeSB.Append("Idle Energy: ");
        StringBuilderUtility.WriteKMG(powerSB, 8, recipe.idleEnergyPerTick * 60, true);
        recipeSB.AppendLine(powerSB.ToString());
        recipeSB.Append("Work Energy: ");
        StringBuilderUtility.WriteKMG(powerSB, 8, recipe.workingEnergyPerTick * 60, true);
        recipeSB.AppendLine(powerSB.ToString());
        recipeSB.Append("Cycle Time: ");
        recipeSB.Append(System.Math.Round(recipe.timeSpend / 60f, 2));
        recipeSB.AppendLine("s");
        recipeText.text = recipeSB.ToString();
      }
      else
      {
        recipeText.text = "";
      }

      return true;
    }

    public override void _OnFree()
    {
      pauseResumeBtn.onClick.RemoveListener(OnPauseResumeBtnClick);
      progressIndicator.SetActive(false);
      blackbox = null;
      titleLocalizer.stringKey = "<Select Blackbox to Inspect>";
    }

    public override void _OnUpdate()
    {
      if (blackbox == null)
        return;

      switch (blackbox.Status)
      {
        case BlackboxStatus.InAnalysis:
          statusText.text = "Analysing";
          statusText.color = idleColor;
          break;
        case BlackboxStatus.AnalysisFailed:
          statusText.text = "Analysis Failed";
          statusText.color = errorColor;
          break;
        case BlackboxStatus.Blackboxed:
          if (blackbox.Simulation != null)
          {
            statusText.text = blackbox.Simulation.isBlackboxSimulating ? "Simulating" : "Simulation Paused";
            statusText.color = blackbox.Simulation.isBlackboxSimulating ? okColor : warningColor;
            break;
          }
          statusText.text = "Blackboxed";
          statusText.color = idleColor;
          break;
        case BlackboxStatus.Invalid:
          statusText.text = "Invalid";
          statusText.color = errorColor;
          break;
        default:
          statusText.text = blackbox.Status.ToString();
          statusText.color = idleColor;
          break;
      }


      if (blackbox.Simulation == null)
      {
        pauseResumeBtn.gameObject.SetActive(false);
        progressImg.fillAmount = 0;
      }
      else
      {
        pauseResumeBtn.gameObject.SetActive(true);
        if (blackbox.Simulation.isBlackboxSimulating)
          pauseResumeBtnText.text = "Pause";
        else
          pauseResumeBtnText.text = "Resume";

        progressImg.fillAmount = blackbox.Simulation.CycleProgress;
      }
    }

    private void OnPauseResumeBtnClick()
    {
      if (blackbox == null)
        return;

      if (blackbox.Status != BlackboxStatus.Blackboxed || blackbox.Simulation == null)
        return;

      if (blackbox.Simulation.isBlackboxSimulating)
        blackbox.Simulation.PauseBlackboxing();
      else
        blackbox.Simulation.ResumeBlackboxing();
    }
  }

  public class ModdedUIBlackboxInspectWindow : IModdedUI<UIBlackboxInspectWindow>
  {
    private GameObject gameObject;
    private UIBlackboxInspectWindow uiBlackboxInspectWindow;

    public UIBlackboxInspectWindow Component => uiBlackboxInspectWindow;
    public GameObject GameObject => gameObject;

    object IModdedUI.Component => uiBlackboxInspectWindow;

    public void CreateUI()
    {
      var windowsObj = UIRoot.instance.uiGame.assemblerWindow.transform.parent;
      
      var inspectWindowObj =
        Create.FancyWindow("Blackbox Inspect Window")
          .ChildOf(windowsObj)
          .WithAnchor(Anchor.TopLeft)
          .OfSize(580, 250)
          .At(100, -100)
          .WithScrollCapture()
          .WithTitle("<Select Blackbox To Inspect>")
          ;

      gameObject = inspectWindowObj.uiElement;

      Select.Text(
          gameObject
            .SelectDescendant("panel-bg", "title-text")
            .GetComponent<Text>()
        )
        .WithFontSize(16);

      var statusLabelText =
        Create.Text("status-label")
          .ChildOf(inspectWindowObj)
          .WithAnchor(Anchor.TopLeft)
          .At(40, -50)
          .OfSize(128, 20)
          .WithFont(UIBuilder.fontSAIRASB)
          .WithFontSize(14)
          .WithMaterial(UIBuilder.materialWidgetTextAlpha5x)
          .WithColor(new Color(0.5882f, 0.5882f, 0.5882f, 1))
          .WithAlignment(TextAnchor.UpperLeft)
          .WithText("Status")
          ;
      
      var progressCircle =
        Create.UIElement("progress-circle")
          .ChildOf(inspectWindowObj)
          .WithAnchor(Anchor.Left)
          .At(40, 0)
          ;
      
      var circleBack = 
        Create.UIElement("circle-back")
          .ChildOf(progressCircle)
          .WithAnchor(Anchor.Left)
          .At(0, 0)
          .OfSize(64, 64)
          .WithComponent((Image x) =>
          {
            x.sprite = UIBuilder.spriteRound64Slice;
            x.color = Color.black.AlphaMultiplied(0.4f);
            x.type = Image.Type.Sliced;
          })
          ;
      
      var circleBorder =
        Create.UIElement("border")
          .ChildOf(circleBack)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          .WithComponent((Image x) =>
          {
            x.sprite = UIBuilder.spriteRound64BorderSlice;
            x.color = new Color(0.6557f, 0.9145f, 1f, 0.1569f);
            x.type = Image.Type.Sliced;
          });
      
      var circleFg =
        Create.UIElement("circle-fg")
          .ChildOf(circleBack)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          .OfSize(-4, -4)
          .WithComponent((Image x) =>
          {
            x.sprite = UIBuilder.spriteCircleThin;
            x.color = new Color(0.9906f, 0.5897f, 0.3691f, 0.7059f);
            x.material = UIBuilder.materialWidgetAlpha5x;
            x.type = Image.Type.Filled;
            x.fillMethod = Image.FillMethod.Radial360;
            x.fillOrigin = 2;
            x.fillAmount = 0.5f;
          });
      
      var pauseResumeBtn =
        Create.Button("pause-resume-btn", "Pause / Resume")
          .ChildOf(inspectWindowObj)
          .WithAnchor(Anchor.BottomLeft)
          .At(40, 40)
          .OfSize(90, 30)
          .WithVisuals((IProperties<Image>) UIBuilder.buttonImgProperties)
          .WithFontSize(14)
          ;
      
      var recipeBox =
        Create.UIElement("recipe-box")
          .ChildOf(inspectWindowObj)
          .WithMinMaxAnchor(new Vector2(0.3f, 0f), new Vector2(0.9f, 0.6f))
          .WithPivot(0.5f, 0f)
          .At(0, 40)
          .OfSize(0, -30)
          ;
      
      var recipeLabel =
        Create.Text("label")
          .ChildOf(recipeBox)
          .WithAnchor(Anchor.TopLeft)
          .WithPivot(0, 0)
          .At(0, 0)
          .WithAlignment(TextAnchor.UpperLeft)
          .OfSize(100, 30)
          .WithFont(UIBuilder.fontSAIRASB)
          .WithText("Recipe")
          ;

      var recipeScrollConfiguration = new ScrollViewConfiguration
      {
        axis = ScrollViewAxis.VerticalOnly,
        scrollBarWidth = 5
      };
      
      var recipeScrollView =
        Create.ScrollView("scroll-view", recipeScrollConfiguration)
          .ChildOf(recipeBox)
          .WithAnchor(Anchor.Stretch)
          .At(0, 0)
          ;
      
      var contentRoot = recipeScrollView.scrollRect.content.gameObject;
      var viewportBg =
        Create.UIElement("bg")
          .ChildOf(recipeScrollView.scrollRect.viewport)
          .WithAnchor(Anchor.Stretch)
          .WithPivot(0, 1)
          .At(0, 0)
          .WithComponent((Image x) => x.color = Color.black.AlphaMultiplied(0.4f));
    
      viewportBg.transform.SetAsFirstSibling();

      Select.VerticalLayoutGroup(contentRoot)
        .WithAnchor(Anchor.Stretch)
        .At(0, 0)
        .WithChildAlignment(TextAnchor.UpperLeft)
        .ForceExpand(width: true, height: false)
        .ChildControls(width: true, height: true)
        .WithContentSizeFitter(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize)
        ;

      var recipeText =
        Create.Text("recipe-text")
          .ChildOf(contentRoot)
          .WithAnchor(Anchor.TopLeft)
          .WithPivot(0, 0)
          .At(0, 0)
          .WithOverflow(HorizontalWrapMode.Wrap, VerticalWrapMode.Overflow)
          .WithAlignment(TextAnchor.UpperLeft)
          .WithLayoutSize(0, 0, flexibleWidth: 1f)
          .WithContentSizeFitter(ContentSizeFitter.FitMode.PreferredSize, ContentSizeFitter.FitMode.PreferredSize)
          ;

      inspectWindowObj.InitializeComponent(out uiBlackboxInspectWindow);
    }

    public void DestroyUI()
    {
      if (uiBlackboxInspectWindow != null)
      {
        uiBlackboxInspectWindow._OnDestroy();
      }
      uiBlackboxInspectWindow = null;
      
      if (gameObject != null)
      {
        Object.Destroy(gameObject);
      }
      gameObject = null;
    }

    public void Free()
    {
      uiBlackboxInspectWindow?._OnFree();
    }

    public void Init()
    {
      
    }

    public void Update()
    {
      uiBlackboxInspectWindow?._OnUpdate();
    }
  }
}
