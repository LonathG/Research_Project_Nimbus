using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
{
    [Header("Cameras")]
    public Camera uiCamera;
    public Camera mainCamera;

    [Header("UI Elements")]
    public GameObject tutorialCanvas;
    public Image displayImage;
    public GameObject previousButton;
    public TMP_Text nextButtonText;

    [Header("Tutorial Content")]
    public Sprite[] tutorialSprites;

    private int currentIndex = 0;

    void Start()
    {
        // 1. Set initial camera state
        uiCamera.gameObject.SetActive(true);
        mainCamera.gameObject.SetActive(false);

        // 2. Ensure UI is active
        tutorialCanvas.SetActive(true);

        // 3. Initialize the first screen
        currentIndex = 0;
        UpdateTutorialUI();
    }

    public void OnNextClicked()
    {
        // If we are not on the last image, go to the next one
        if (currentIndex < tutorialSprites.Length - 1)
        {
            currentIndex++;
            UpdateTutorialUI();
        }
        else // If we ARE on the last image, this button acts as "Finish"
        {
            CloseTutorial();
        }
    }

    public void OnPreviousClicked()
    {
        // Go back one image
        if (currentIndex > 0)
        {
            currentIndex--;
            UpdateTutorialUI();
        }
    }

    public void OnSkipClicked()
    {
        CloseTutorial();
    }

    private void UpdateTutorialUI()
    {
        // Update the main image
        if (tutorialSprites.Length > 0)
        {
            displayImage.sprite = tutorialSprites[currentIndex];
        }

        // Toggle the Previous button's visibility
        previousButton.SetActive(currentIndex > 0);

        // Change the Next button text based on the current index
        if (currentIndex == tutorialSprites.Length - 1)
        {
            nextButtonText.text = "Finish!";
        }
        else
        {
            nextButtonText.text = "Next";
        }
    }

    private void CloseTutorial()
    {
        // Hide the UI
        tutorialCanvas.SetActive(false);

        // Switch to the main game camera
        uiCamera.gameObject.SetActive(false);
        mainCamera.gameObject.SetActive(true);
    }
}