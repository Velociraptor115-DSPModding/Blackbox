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

    private Button pauseResumeBtn;
    private Image progressImg;
    private GameObject progressIndicator;

    private static Color errorColor = new Color(1f, 0.27f, 0.1934f, 0.7333f);
    private static Color warningColor = new Color(0.9906f, 0.5897f, 0.3691f, 0.7059f);
    private static Color okColor = new Color(0.3821f, 0.8455f, 1f, 0.7059f);
    private static Color idleColor = new Color(0.5882f, 0.5882f, 0.5882f, 0.8196f);

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
