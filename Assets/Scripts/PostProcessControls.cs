using UnityEngine;
using UnityEngine.Rendering;

public class PostProcess : MonoBehaviour
{

    [SerializeField] private Volume postProcessingVolume;
    [SerializeField] private bool disable;

    [Header("Post Processing Profiles")]
    [SerializeField] private VolumeProfile postProfileMain;
    [SerializeField] private VolumeProfile postProfileSecondary;

    public void MainPostProcess()

    { 
        postProcessingVolume.profile = postProfileMain;
    }

    public void SecondaryPostProcess()

    { 
        postProcessingVolume.profile = postProfileSecondary;
    }

    public void DisablePostProcess()
    {
        disable = !disable;
        postProcessingVolume.enabled = disable;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
