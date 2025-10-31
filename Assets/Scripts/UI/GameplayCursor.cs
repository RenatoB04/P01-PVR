using UnityEngine;

public static class GameplayCursor
{
    // Bloqueia o cursor ao centro e oculta (gameplay FPS)
    public static void Lock()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Liberta o cursor e mostra (UI/menus)
    public static void Unlock()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}