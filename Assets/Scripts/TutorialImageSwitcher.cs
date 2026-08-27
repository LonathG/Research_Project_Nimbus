using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TutorialImageSwitcher : MonoBehaviour
{
    [Header("Where to display the images")]
    public Image tutorialImageDisplay;

    [Header("The Image Files (Sprites)")]
    public Sprite firstImage;
    public Sprite secondImage;

    [Header("Timing Settings")]
    public float switchDelay = 5.0f;
    public float lifetimeAfterSwitch = 10.0f; // Editable in Inspector!

    void Start()
    {
        // 1. Instantly display the first image
        if (tutorialImageDisplay != null && firstImage != null)
        {
            tutorialImageDisplay.sprite = firstImage;
        }

        // 2. Start the automated sequence
        StartCoroutine(TutorialSequence());
    }

    IEnumerator TutorialSequence()
    {
        // 3. Wait for the initial 5 seconds
        yield return new WaitForSeconds(switchDelay);

        // 4. Swap to the second image (image_f124c9)
        if (tutorialImageDisplay != null && secondImage != null)
        {
            tutorialImageDisplay.sprite = secondImage;
        }

        // 5. Wait for the final 10 seconds
        yield return new WaitForSeconds(lifetimeAfterSwitch);

        // 6. Turn off this GameObject entirely to hide the UI
        gameObject.SetActive(false);
    }
}