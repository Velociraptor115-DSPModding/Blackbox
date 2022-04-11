using UnityEngine;

namespace DysonSphereProgram.Modding.Blackbox.UI
{
  public interface IModdedUI
  {
    void CreateUI();
    void DestroyUI();

    void Init();
    void Free();

    void Update();


    object Component { get; }
    GameObject GameObject { get; }
  }

  public interface IModdedUI<T> : IModdedUI where T: MonoBehaviour
  {
    new T Component { get; }
  }
}
