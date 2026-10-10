using UnityEngine;
using UnityEngine.SceneManagement;

public static class Utilities
{
    public static int playerDeaths = 0;
    public static string UpdateDeathCount(ref int counterRef)
    {
        counterRef += 1;
        return "Next u'll be at number" + counterRef;
    }
    //use it in case when player die
    public static void CountDeathAndRestart(int sceneIndex = 0)
    {
        string message = UpdateDeathCount(ref playerDeaths);
        Debug.Log(message);
        Debug.Log("Player deaths:" + playerDeaths);
        RestartLevel(sceneIndex);
    }
    public static void RestartLevel(int sceneIndex = 0)
    {
        //снимаем паузу до загрузки, иначе новая сцена стартует замороженной
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneIndex);
    }
}
