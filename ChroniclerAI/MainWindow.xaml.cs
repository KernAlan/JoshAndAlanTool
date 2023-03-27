﻿using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChroniclerAI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string? _apiKey;
        private string? _audioFilePath;
        private List<string> _outputText = new List<string>();
        private static string _recordedAudioFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recordedaudio.mp3");
        private AudioRecorder _recorder;
        private bool _isRecording;
        private HttpClient _client = new HttpClient();

        public string? ApiKey
        {
            get => _apiKey;
            set
            {
                if (_apiKey != value)
                {
                    _apiKey = value;

                    // Trim the API key if it has leading or trailing whitespace
                    if (_apiKey is not null)
                    {
                        _apiKey = _apiKey.Trim();
                    }

                    if (_apiKey is not null && _apiKey.Length > 0)
                    {
                        SaveApiKeyToFile(_apiKey);
                    }

                    OnPropertyChanged(nameof(ApiKey));
                }
            }
        }
        
        public string? AudioFilePath
        {
            get => _audioFilePath;
            set
            {
                if (_audioFilePath != value)
                {
                    _audioFilePath = value;
                    OnPropertyChanged(nameof(AudioFilePath));
                }
            }
        }

        public List<string> OutputText
        {
            get => _outputText;
            set
            {
                if (_outputText != value)
                {
                    _outputText = value;
                    OnPropertyChanged(nameof(OutputText));
                }
            }
        }
        
        public string OutputTextString
        {
            get => string.Join(Environment.NewLine, _outputText);
            set
            {
                OutputText = value.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadApiKeyFromFile();
            _isRecording = false;
            _recorder = new AudioRecorder(_recordedAudioFilePath);
            _client.Timeout = TimeSpan.FromSeconds(600);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            return;
        }

        private async void Transcribe(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(AudioFilePath))
                {
                    throw new ArgumentNullException("No audio file detected!");
                }
                TranscribeButton.IsEnabled = false;
                TranscribeButton.Content = "Transcribing...";
                var result = "";

                MessageBoxResult initConfirmation = MessageBox.Show("Confirming you would like to transcribe the selected audio file?",
                        "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (initConfirmation == MessageBoxResult.No)
                {
                    return;
                }

                // If the audio file exceeds 25mb, confirm to the user, and split it into 10 min files, and process each one individualls
                if (new FileInfo(AudioFilePath).Length > 25 * 1024 * 1024)
                {
                    MessageBoxResult confirmation = MessageBox.Show("This file is over 25mb. You can either split this file yourself, or Chronicler will split it into 10 min copy chunks for you. Is this OK?",
                        "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (confirmation == MessageBoxResult.No)
                    {
                        return;
                    }
                    var splitAudioFilePaths = SplitFile();
                    foreach (var file in splitAudioFilePaths)
                    {
                        AudioFilePath = file;
                        result = await ProcessTranscribe();
                        OutputText.Add(result);
                    }
                }
                else
                {
                    result = await ProcessTranscribe();
                    OutputText.Add(result);
                }
                
                if (result is null)
                {
                    throw new ArgumentNullException("Transcription returned empty!");
                }
                
                OnPropertyChanged(nameof(OutputText));
                UpdateOutputTextBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Transcription failed: {ex.Message}");
            }
            finally
            {
                TranscribeButton.IsEnabled = true;
                TranscribeButton.Content = "Transcribe";
            }
        }

        private async Task<string> ProcessTranscribe()
        {
            try
            {
                if (AudioFilePath is null)
                {
                    throw new ArgumentNullException("No audio file detected!");
                }

                if (ApiKey is null)
                {
                    throw new ArgumentNullException("No API key detected!");
                }
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StreamContent(File.OpenRead(AudioFilePath)), "file", System.IO.Path.GetFileName(AudioFilePath));
                    content.Add(new StringContent("whisper-1"), "model");
                    content.Add(new StringContent("text"), "response_format");

                    using (var response = await _client.PostAsync("https://api.openai.com/v1/audio/transcriptions", content))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            return responseContent;
                        }
                        else
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            throw new Exception($"Request failed with status code {response.StatusCode}: {responseContent}");
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
           
        }

        private void UpdateOutputTextBox()
        {
            OnPropertyChanged(nameof(OutputTextString));
        }
        
        private void Browse(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
            {
                AudioFilePath = openFileDialog.FileName;
                OnPropertyChanged(nameof(AudioFilePath));
            }
        }

        public void SaveApiKeyToFile(string apiKey)
        {
            try
            {
                File.WriteAllText("apiKey.txt", apiKey);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API Key save failed: {ex.Message}");
            }
        }

        public void LoadApiKeyFromFile()
        {
            try
            {
                string apiKeyFromFile = File.ReadAllText("apiKey.txt");
                ApiKey = apiKeyFromFile;
            }
            catch (Exception)
            {
                // If we catch an error reading the file, assume this is the first time the app has been run, and create the initial .txt file
                SaveApiKeyToFile("API Key");
            }

        }

        public void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_isRecording)
                {
                    _recorder.StartRecording();
                    RecordButton.Content = "Stop Recording";
                    _isRecording = true;
                }
                else
                {
                    StopRecording(sender, e);
                    RecordButton.Content = "Start Recording";
                    _isRecording = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start recording: {ex.Message}");
            }
            
        }
        
        public void StopRecording(object sender, RoutedEventArgs e)
        {
            try
            {
                _recorder.StopRecording();
                AudioFilePath = _recordedAudioFilePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop recording: {ex.Message}");
            }
        }

        public List<string> SplitFile()
        {
            try
            {
                if (AudioFilePath is null)
                {
                    throw new ArgumentNullException("Cannot split an empty or nonexistent file.");
                }
                
                var audioSplitter = new AudioSplitter();
                var splitFilesList = audioSplitter.SplitAudio(AudioFilePath, Directory.GetCurrentDirectory(), TimeSpan.FromMinutes(10));
                return splitFilesList;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to split file: {ex.Message}");
                return new List<string>();
            }
        }

        public async void Summarize(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApiKey is null)
                {
                    throw new ArgumentNullException("API Key must not be empty.");
                }

                if (OutputText is null)
                {
                    throw new ArgumentNullException("Nothing to summarize.");
                }

                SummarizeButton.IsEnabled = false;
                SummarizeButton.Content = "Summarizing...";
                var chatGptRepo = new ChatGptApiClient(ApiKey);
                var summary = "SUMMARY: \r\n" + await chatGptRepo.GenerateCompletion(OutputText, ECompletionType.Summarize);
                
                if (string.IsNullOrEmpty(summary))
                {
                    throw new ArgumentNullException("Summary was empty.");
                }

                OutputText.Add(summary);
                OnPropertyChanged(nameof(OutputText));
                UpdateOutputTextBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to summarize: {ex.Message}");
            }
            finally
            {

            }
            SummarizeButton.IsEnabled = true;
            SummarizeButton.Content = "Summarize";
        }

        public async void Highlight(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApiKey is null)
                {
                    throw new ArgumentNullException("API Key must not be empty.");
                }

                if (OutputText is null)
                {
                    throw new ArgumentNullException("Error: Nothing to highlight.");
                }

                HighlightButton.IsEnabled = false;
                HighlightButton.Content = "Highlighting...";
                var chatGptRepo = new ChatGptApiClient(ApiKey);
                var highlights = "HIGHLIGHTS: \r\n" + await chatGptRepo.GenerateCompletion(OutputText, ECompletionType.Highlight);

                if (string.IsNullOrEmpty(highlights))
                {
                    throw new ArgumentNullException("Highlights were empty.");
                }

                OutputText.Add(highlights);
                OnPropertyChanged(nameof(OutputText));
                UpdateOutputTextBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to highlight: {ex.Message}");
            }
            finally
            {

            }
            HighlightButton.IsEnabled = true;
            HighlightButton.Content = "Highlight";
        }

        public async void Enumerate(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApiKey is null)
                {
                    throw new ArgumentNullException("API Key must not be empty.");
                }

                if (OutputText is null)
                {
                    throw new ArgumentNullException("Error: Nothing to Enumerate.");
                }

                EnumerateButton.IsEnabled = false;
                EnumerateButton.Content = "Enumerating...";
                var chatGptRepo = new ChatGptApiClient(ApiKey);
                var enumerations = "ENUMERATIONS: \r\n" + await chatGptRepo.GenerateCompletion(OutputText, ECompletionType.Enumerate);

                if (string.IsNullOrEmpty(enumerations))
                {
                    throw new ArgumentNullException("Enumerations were empty.");
                }

                OutputText.Add(enumerations);
                OnPropertyChanged(nameof(OutputText));
                UpdateOutputTextBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to enumerate: {ex.Message}");
            }
            finally
            {

            }
            EnumerateButton.IsEnabled = true;
            EnumerateButton.Content = "Enumerate";
        }

        public async void Ask(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ApiKey is null)
                {
                    throw new ArgumentNullException("API Key must not be empty.");
                }

                if (OutputText is null)
                {
                    throw new ArgumentNullException("There is nothing to ask of in the text box.");
                }

                var inputString = Microsoft.VisualBasic.Interaction.InputBox("Enter your question or command here", "Custom Ask or Command", "");
                
                if (string.IsNullOrEmpty(inputString))
                {
                    throw new InvalidOperationException("No question or command was input.");
                }
                
                if (inputString.Count() > 256)
                {
                    throw new InvalidOperationException("Input was too long. Keep the input to a maximum of 256 characters");
                }

                AskButton.IsEnabled = false;
                AskButton.Content = "Asking...";
                var chatGptRepo = new ChatGptApiClient(ApiKey);
                var response = "RESPONSE: \r\n" + await chatGptRepo.GenerateCompletion(OutputText, ECompletionType.Ask);

                if (string.IsNullOrEmpty(response))
                {
                    throw new ArgumentNullException("Response was empty.");
                }

                OutputText.Add(response);
                OnPropertyChanged(nameof(OutputText));
                UpdateOutputTextBox();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to respond: {ex.Message}");
            }
            finally
            {

            }
            AskButton.IsEnabled = true;
            AskButton.Content = "Ask";
        }

        public void OpenAIAPIKeys(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://platform.openai.com/account/api-keys";
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open OpenAI API Keys page: {ex.Message}");
            }
        }

        public void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Copyright © 2023 Alan Kern. This software was a labor of love provided free of charge and open-sourced via an MIT license.");

        }
        
        public void Donate_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://www.paypal.com/donate/?business=7CEJDV8VQ9BTL&no_recurring=0&currency_code=USD";
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        public void Readme_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://github.com/KernAlan/ChroniclerAI";
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            OutputText = new List<string>();
            OnPropertyChanged(nameof(OutputText));
            UpdateOutputTextBox();
        }
    }
}
