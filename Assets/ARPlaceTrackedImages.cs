using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System;
using System.Linq;
using TMPro;

public class ARPlaceTrackedImages : MonoBehaviour
{
    public GameObject EmailPlane;
    public GameObject InstructionsUI;
    public GameObject SoundsOkayUI;
    public GameObject OptionsUI;
    public GameObject CorrectLabel;
    public GameObject IncorrectLabel;
    public TextAsset EmailData;
    public Camera Camera;

    private readonly Dictionary<string, Tuple<GameObject, EmailInfoCollection.EmailInfo>> instantiatedEmails
        = new Dictionary<string, Tuple<GameObject, EmailInfoCollection.EmailInfo>>();
    private EmailInfoCollection loadedEmails;
    private EmailInfoCollection.EmailInfo activeEmail;
    private ARTrackedImageManager trackedImagesManager;

    void Awake()
    {
        trackedImagesManager = GetComponent<ARTrackedImageManager>();

        // load the email config
        loadedEmails = JsonUtility.FromJson<EmailInfoCollection>(EmailData.text);

        var yesButton = SoundsOkayUI.transform.GetChild(1)?.GetComponent<Button>();
        yesButton.onClick.AddListener(SoundsOkayYesClicked);

        var noButton = SoundsOkayUI.transform.GetChild(2)?.GetComponent<Button>();
        noButton.onClick.AddListener(SoundsOkayNoClicked);

        var submitButton = OptionsUI.transform.GetChild(1)?.GetComponent<Button>();
        submitButton.onClick.AddListener(OptionsSubmitClicked);
    }

    void OnEnable()
    {
        trackedImagesManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    void OnDisable()
    {
        trackedImagesManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        // go through each recognized image and check for them in the email config
        foreach (var trackedImage in eventArgs.added)
        {
            var imageName = trackedImage.referenceImage.name;

            foreach (var email in loadedEmails.items)
            {
                if (string.Compare(email.name, imageName, StringComparison.Ordinal) == 0
                    && !instantiatedEmails.ContainsKey(imageName))
                {
                    // email found; create it and update the UI
                    var newPrefab = CreateVREmail(trackedImage, email);
                    instantiatedEmails[imageName] = new Tuple<GameObject, EmailInfoCollection.EmailInfo>(newPrefab, email);
                }
            }
        }

        // Go through images with state changes
        foreach (var trackedImage in eventArgs.updated)
        {
            var trackedItem = instantiatedEmails[trackedImage.referenceImage.name];
            trackedItem.Item1.SetActive(trackedImage.trackingState == TrackingState.Tracking);
        }

        // go through items which the tracker has deemed removed
        foreach (var trackedImage in eventArgs.removed)
        {
            Destroy(instantiatedEmails[trackedImage.referenceImage.name].Item1);
            instantiatedEmails.Remove(trackedImage.referenceImage.name);
        }

        // update the active email for the UI to display correctly
        SetActiveEmail(instantiatedEmails.Values.FirstOrDefault(v => v.Item1.activeSelf)?.Item2);
    }

    // We have one "active email" at a time. This is the one that appears on-screen for the UI.
    // Note that this could result in a race condition if multiple emails appear onscreen at the same time.
    private void SetActiveEmail(EmailInfoCollection.EmailInfo email)
    {
        if (email == null)
        {
            StopQuestionUI();
        }
        else if (activeEmail == null || email.name != activeEmail.name)
        {
            Debug.Log(email.name);
            StartQuestionUI(email);
        }

        activeEmail = email;
    }

    private void StartQuestionUI(EmailInfoCollection.EmailInfo email)
    {
        var text = SoundsOkayUI.transform.GetChild(0)?.GetComponent<TMPro.TextMeshProUGUI>();
        if (text != null)
        {
            text.text = email.question;
        }

        SetAllUIsInactive();
        SoundsOkayUI.SetActive(true);
    }

    private void SetAllUIsInactive()
    {
        InstructionsUI.SetActive(false);
        SoundsOkayUI.SetActive(false);
        OptionsUI.SetActive(false);
        CorrectLabel.SetActive(false);
        IncorrectLabel.SetActive(false);
    }

    private void StopQuestionUI()
    {
        SetAllUIsInactive();
        InstructionsUI.SetActive(true);
    }

    public void SoundsOkayYesClicked()
    {
        if (activeEmail != null)
        {
            SetAllUIsInactive();
            if (activeEmail.correctOption == null)
            {
                CorrectLabel.SetActive(true);
            }
            else
            {
                IncorrectLabel.SetActive(true);
            }
        }
    }

    public void SoundsOkayNoClicked()
    {
        if (activeEmail != null)
        {
            SetAllUIsInactive();
            OptionsUI.SetActive(true);
            var dropdown = OptionsUI.transform.GetChild(2)?.GetComponent<TMP_Dropdown>();
            if (dropdown != null)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(activeEmail.options.ToList());
            }
        }
    }

    public void OptionsSubmitClicked()
    {
        if (activeEmail != null)
        {
            var dropdown = OptionsUI.transform.GetChild(2)?.GetComponent<TMP_Dropdown>();
            if (dropdown != null && dropdown.options[dropdown.value].text == activeEmail.correctOption)
            {
                SetAllUIsInactive();
                CorrectLabel.SetActive(true); 
            }
            else
            {
                SetAllUIsInactive();
                IncorrectLabel.SetActive(true);
            }
        }
    }

    private GameObject CreateVREmail(ARTrackedImage trackedImage, EmailInfoCollection.EmailInfo email)
    {
        var newPrefab = Instantiate(EmailPlane, trackedImage.transform) as GameObject;
        var canvas = newPrefab.transform.GetChild(0).GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.worldCamera = Camera;

            var title = canvas.transform.GetChild(0)?.GetComponent<TMPro.TextMeshProUGUI>();
            var body = canvas.transform.GetChild(1)?.GetComponent<TMPro.TextMeshProUGUI>();

            if (title != null)
            {
                title.text = email.subject;
            }
            if (body != null)
            {
                body.text = email.body;
            }
        }

        return newPrefab;
    }

    [Serializable]
    private class EmailInfoCollection
    {
        public EmailInfo[] items;

        [Serializable]
        public class EmailInfo
        {
            public string name;
            public string subject;
            public string body;
            public string question;
            public string[] options;
            public string correctOption;
        }
    }
}
