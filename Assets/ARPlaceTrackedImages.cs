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
    public GameObject OptionsUI;
    public GameObject ScoreUI;
    public GameObject CorrectLabel;
    public GameObject IncorrectLabel;
    public TextAsset EmailData;
    public Camera Camera;

    private readonly Dictionary<string, Tuple<GameObject, QuestionCollection.QuestionInfo>> instantiatedEmails
        = new Dictionary<string, Tuple<GameObject, QuestionCollection.QuestionInfo>>();
    private QuestionCollection loadedEmails;
    private QuestionCollection.QuestionInfo activeEmail;
    private ARTrackedImageManager trackedImagesManager;

    private IDictionary<string, QuestionState> questionStates = new Dictionary<string, QuestionState>();

    void Awake()
    {
        trackedImagesManager = GetComponent<ARTrackedImageManager>();

        // load the email config
        loadedEmails = JsonUtility.FromJson<QuestionCollection>(EmailData.text);
        foreach (var question in loadedEmails.items)
        {
            questionStates[question.name] = new QuestionState
            {
                questionName = question.name,
                order = Enumerable.Range(0, question.scenarios.Length).OrderBy(i => UnityEngine.Random.value).ToArray()
            };
        }

        var submitButton = OptionsUI.transform.GetChild(1)?.GetComponent<Button>();
        submitButton.onClick.AddListener(OptionsSubmitClicked);

        var tryAgain = IncorrectLabel.transform.GetChild(1)?.GetComponent<Button>();
        tryAgain.onClick.AddListener(TryAgainClicked);
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
                    instantiatedEmails[imageName] = new Tuple<GameObject, QuestionCollection.QuestionInfo>(newPrefab, email);
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
    private void SetActiveEmail(QuestionCollection.QuestionInfo email)
    {
        if (email == null)
        {
            StopQuestionUI();
        }
        else if (activeEmail == null || email.name != activeEmail.name)
        {
            StartQuestionUI(email);
        }

        activeEmail = email;
    }

    private void StartQuestionUI(QuestionCollection.QuestionInfo email)
    {
        SetAllUIsInactive();
        OptionsUI.SetActive(true);
        var questionText = OptionsUI.transform.GetChild(0)?.GetComponent<TMPro.TextMeshProUGUI>();
        if (questionText != null)
        {
            questionText.text = email.question;
        }

        var dropdown = OptionsUI.transform.GetChild(2)?.GetComponent<TMP_Dropdown>();
        if (dropdown != null && questionStates.TryGetValue(email.name, out var questionState))
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(email.scenarios[questionState.order[questionState.currentTry]].options.ToList());
        }
    }

    private void SetAllUIsInactive()
    {
        InstructionsUI.SetActive(false);
        OptionsUI.SetActive(false);
        CorrectLabel.SetActive(false);
        IncorrectLabel.SetActive(false);
    }

    private void StopQuestionUI()
    {
        SetAllUIsInactive();
        InstructionsUI.SetActive(true);
    }

    public void TryAgainClicked()
    {
        if (instantiatedEmails.TryGetValue(activeEmail.name, out var email)
            && questionStates.TryGetValue(activeEmail.name, out var questionState))
        {
            StartQuestionUI(activeEmail);
            var canvas = email.Item1.transform.GetChild(0).GetComponent<Canvas>();
            var body = canvas.transform.GetChild(0)?.GetComponent<TMPro.TextMeshProUGUI>();
            body.text = activeEmail.scenarios[questionState.order[questionState.currentTry]].background;
        }
    }

    public void OptionsSubmitClicked()
    {
        if (activeEmail != null && questionStates.TryGetValue(activeEmail.name, out var questionState))
        {
            var currentScenario = activeEmail.scenarios[questionState.order[questionState.currentTry]];
            var dropdown = OptionsUI.transform.GetChild(2)?.GetComponent<TMP_Dropdown>();
            if (dropdown != null && dropdown.options[dropdown.value].text == currentScenario.correct)
            {
                SetAllUIsInactive();
                CorrectLabel.SetActive(true);
                questionState.successful = true;
                var scoreText = ScoreUI.transform.GetChild(1)?.GetComponent<TMPro.TextMeshProUGUI>();
                if (scoreText != null)
                {
                    scoreText.text = questionStates.Count(s => s.Value.successful).ToString()
                        + "/"
                        + questionStates.Count.ToString();
                }
            }
            else
            {
                SetAllUIsInactive();
                IncorrectLabel.SetActive(true);
                questionState.currentTry = (questionState.currentTry + 1) % activeEmail.scenarios.Length;
            }
        }
    }

    private GameObject CreateVREmail(ARTrackedImage trackedImage, QuestionCollection.QuestionInfo email)
    {
        var newPrefab = Instantiate(EmailPlane, trackedImage.transform) as GameObject;
        var canvas = newPrefab.transform.GetChild(0).GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.worldCamera = Camera;

            var body = canvas.transform.GetChild(0)?.GetComponent<TMPro.TextMeshProUGUI>();

            if (body != null && questionStates.TryGetValue(email.name, out var questionState))
            {
                body.text = email.scenarios[questionState.order[questionState.currentTry]].background;
            }
        }

        return newPrefab;
    }

    [Serializable]
    private class QuestionCollection
    {
        public QuestionInfo[] items;

        [Serializable]
        public class QuestionInfo
        {
            public string name;
            public string question;
            public Scenario[] scenarios;
        }

        [Serializable]
        public class Scenario
        {
            public string background;
            public string[] options;
            public string correct;
        }
    }

    private class QuestionState
    {
        public string questionName;
        public int[] order;
        public int currentTry = 0;
        public bool successful = false;
    }
}
