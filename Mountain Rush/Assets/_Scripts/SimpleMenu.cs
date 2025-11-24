using UnityEngine;
using UnityEngine.SceneManagement; // Нужно для работы со сценами
using UnityEngine.UI; // Нужно для работы со Слайдером

public class SimpleMenu : MonoBehaviour
{
    [Header("Настройки")]
    public string gameSceneName = "Game"; // Точное имя твоей игровой сцены
    public Slider volumeSlider; // Ссылка на ползунок

    void Start()
    {
        // 1. Загружаем сохраненную громкость (по умолчанию 1.0, то есть 100%)
        float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
        
        // 2. Применяем громкость к игре
        AudioListener.volume = savedVolume;

        // 3. Ставим ползунок в правильное положение
        if (volumeSlider != null)
        {
            volumeSlider.value = savedVolume;
        }
    }

    // Метод для кнопки "Играть"
    public void PlayGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    // Метод для кнопки "Выход"
    public void QuitGame()
    {
        Application.Quit();
        Debug.Log("Выход из игры (сработает только в скомпилированной игре)");
    }

    // Метод для ползунка громкости
    public void SetVolume(float volume)
    {
        AudioListener.volume = volume; // Меняет глобальную громкость
        PlayerPrefs.SetFloat("MasterVolume", volume); // Сохраняет значение на диск
        PlayerPrefs.Save();
    }
}