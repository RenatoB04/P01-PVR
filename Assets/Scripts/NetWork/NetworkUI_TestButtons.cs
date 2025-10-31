using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class NetworkUI_TestButtons : MonoBehaviour
{
    public Button hostButton;
    public Button clientButton;
    public Button serverButton;

    private static bool hasStartedNetwork = false;

    void Start()
    {
        // Se já foi iniciado, esconde logo
        if (hasStartedNetwork)
        {
            HideButtons();
            return;
        }

        if (hostButton) hostButton.onClick.AddListener(StartHost);
        if (clientButton) clientButton.onClick.AddListener(StartClient);
        if (serverButton) serverButton.onClick.AddListener(StartServer);
    }

    private void StartHost()
    {
        Debug.Log("Starting Host...");
        NetworkManager.Singleton.StartHost();
        hasStartedNetwork = true;
        HideButtons();

        // Se quiseres mudar de cena automaticamente:
        if (NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.LoadScene("Prototype", LoadSceneMode.Single);
    }

    private void StartClient()
    {
        Debug.Log("Starting Client...");
        NetworkManager.Singleton.StartClient();
        hasStartedNetwork = true;
        HideButtons();
    }

    private void StartServer()
    {
        Debug.Log("Starting Server...");
        NetworkManager.Singleton.StartServer();
        hasStartedNetwork = true;
        HideButtons();

        if (NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.LoadScene("Prototype", LoadSceneMode.Single);
    }

    private void HideButtons()
    {
        if (hostButton) hostButton.gameObject.SetActive(false);
        if (clientButton) clientButton.gameObject.SetActive(false);
        if (serverButton) serverButton.gameObject.SetActive(false);
    }
}
