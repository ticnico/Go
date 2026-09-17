using System;
using System.Windows;
using System.Windows.Controls;
using GoldenBullet.Views.Pages;

namespace GoldenBullet.Views.Pages.Cookies
{
    public partial class CookieCheckerHostPage : Page
    {
        private NetflixCookiePage _netflixCookiePage;
        private NFTokenCheckerPage _nfTokenCheckerPage;
        private string _currentChecker = "NetflixCookie";

        public CookieCheckerHostPage()
        {
            InitializeComponent();
            Loaded += CookieCheckerHostPage_Loaded;
        }

        private void CookieCheckerHostPage_Loaded(object sender, RoutedEventArgs e)
        {
            SwitchChecker("NetflixCookie");
        }

        public void SwitchChecker(string checkerType)
        {
            _currentChecker = checkerType;

            // Update ComboBox without triggering event
            foreach (ComboBoxItem item in CheckerComboBox.Items)
            {
                if (item.Tag?.ToString() == checkerType)
                {
                    if (CheckerComboBox.SelectedItem != item)
                    {
                        CheckerComboBox.SelectedItem = item;
                    }
                    break;
                }
            }

            // Navigate to appropriate page
            switch (checkerType)
            {
                case "NetflixCookie":
                    if (_netflixCookiePage == null)
                    {
                        _netflixCookiePage = new NetflixCookiePage();
                    }
                    ContentFrame.Content = _netflixCookiePage;
                    break;

                case "NFTokenChecker":
                    if (_nfTokenCheckerPage == null)
                    {
                        _nfTokenCheckerPage = new NFTokenCheckerPage();
                    }
                    ContentFrame.Content = _nfTokenCheckerPage;
                    break;
            }
        }

        private void CheckerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CheckerComboBox.SelectedItem is ComboBoxItem item && item.Tag is string checkerType)
            {
                if (checkerType != _currentChecker)
                {
                    SwitchChecker(checkerType);
                }
            }
        }
    }
}
