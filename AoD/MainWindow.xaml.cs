using NAudio.Wave;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AlwaysOnDisplay
{
    public partial class MainWindow : Window
    {
        private WaveInEvent? _waveIn;

        private readonly DispatcherTimer _idleLifeTimer = new();
        private readonly Random _random = new();

        private const double BaseHeight = 290;
        private const double MaxLookX = 155;
        private const double MaxLookY = 42;

        private double _smoothedLeft = 0.0;
        private double _smoothedRight = 0.0;
        private double _smoothedTotal = 0.0;
        private double _previousTotal = 0.0;

        private DateTime _lastSoundTime = DateTime.MinValue;
        private DateTime _lastScaredTime = DateTime.MinValue;
        private DateTime _lastIdleGlanceTime = DateTime.MinValue;
        private DateTime _calmUntil = DateTime.MinValue;

        private readonly DispatcherTimer _idleCheckTimer = new();
        private readonly AppManager _appManager = new();

        private bool _isAodActive = false;
        private readonly TimeSpan _idleThreshold = TimeSpan.FromMinutes(7);

        private bool _isInScareSequence = false;

        public MainWindow()
        {
            InitializeComponent();

            Hide();

            SetNeutral(immediate: true);
            StartIdleLife();
            StartMicrophone();

            _idleCheckTimer.Interval = TimeSpan.FromSeconds(1);
            _idleCheckTimer.Tick += IdleCheckTimer_Tick;
            _idleCheckTimer.Start();
        }

        private void IdleCheckTimer_Tick(object? sender, EventArgs e)
        {
            var idle = IdleDetector.GetIdleTime();

            if (!_isAodActive && idle >= _idleThreshold)
            {
                ActivateAodMode();
                return;
            }

            if (_isAodActive && idle < TimeSpan.FromMilliseconds(500))
            {
                DeactivateAodMode();
            }
        }

        private void ActivateAodMode()
        {
            _isAodActive = true;

            _appManager.EnterSemiSleep();

            Show();
            WindowState = WindowState.Maximized;
            Activate();
            Topmost = true;
        }

        private void DeactivateAodMode()
        {
            _isAodActive = false;

            Hide();
            _appManager.ExitSemiSleep();
        }

        private void StartIdleLife()
        {
            _idleLifeTimer.Interval = TimeSpan.FromMilliseconds(1300);
            _idleLifeTimer.Tick += IdleLifeTimer_Tick;
            _idleLifeTimer.Start();
        }

        private void IdleLifeTimer_Tick(object? sender, EventArgs e)
        {
            if (_isInScareSequence)
                return;

            if ((DateTime.Now - _lastSoundTime).TotalMilliseconds < 1400)
                return;

            if (DateTime.Now < _calmUntil)
            {
                CalmBreathing();
                return;
            }

            if ((DateTime.Now - _lastIdleGlanceTime).TotalMilliseconds < 2400)
                return;

            _lastIdleGlanceTime = DateTime.Now;

            int mode = _random.Next(0, 6);

            switch (mode)
            {
                case 0:
                    LookIdleLeft();
                    break;
                case 1:
                    LookIdleRight();
                    break;
                case 2:
                    LookIdleLeftUp();
                    break;
                case 3:
                    LookIdleRightUp();
                    break;
                case 4:
                    CalmBreathing();
                    break;
                default:
                    SettleCenter();
                    break;
            }
        }

        private void StartMicrophone()
        {
            try
            {
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(44100, 16, 2),
                    BufferMilliseconds = 35
                };

                _waveIn.DataAvailable += WaveIn_DataAvailable;
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось запустить микрофон:\n" + ex.Message,
                    "Ошибка микрофона",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_waveIn == null)
                return;

            int bytesPerSample = _waveIn.WaveFormat.BitsPerSample / 8;
            int channels = _waveIn.WaveFormat.Channels;
            int blockAlign = _waveIn.WaveFormat.BlockAlign;

            if (bytesPerSample != 2 || blockAlign <= 0)
                return;

            double sumLeft = 0.0;
            double sumRight = 0.0;
            int frames = 0;

            for (int i = 0; i <= e.BytesRecorded - blockAlign; i += blockAlign)
            {
                short leftSample = BitConverter.ToInt16(e.Buffer, i);
                double left = leftSample / 32768.0;

                double right;
                if (channels >= 2)
                {
                    short rightSample = BitConverter.ToInt16(e.Buffer, i + bytesPerSample);
                    right = rightSample / 32768.0;
                }
                else
                {
                    right = left;
                }

                sumLeft += left * left;
                sumRight += right * right;
                frames++;
            }

            if (frames == 0)
                return;

            double leftRms = Math.Sqrt(sumLeft / frames);
            double rightRms = Math.Sqrt(sumRight / frames);
            double totalRms = (leftRms + rightRms) / 2.0;

            _smoothedLeft = Lerp(_smoothedLeft, leftRms, 0.48);
            _smoothedRight = Lerp(_smoothedRight, rightRms, 0.48);
            _smoothedTotal = Lerp(_smoothedTotal, totalRms, 0.48);

            Dispatcher.Invoke(() =>
            {
                ReactToSound(_smoothedLeft, _smoothedRight, _smoothedTotal, channels);
            });
        }

        private void ReactToSound(double leftLevel, double rightLevel, double totalLevel, int channels)
        {
            if (_isInScareSequence)
                return;

            double delta = totalLevel - _previousTotal;
            _previousTotal = totalLevel;

            bool stereo = channels >= 2;
            bool hasSound = totalLevel > 0.0065;
            bool suddenSound = delta > 0.020 || totalLevel > 0.085;

            if (hasSound)
                _lastSoundTime = DateTime.Now;

            if (DateTime.Now < _calmUntil && !suddenSound)
                return;

            if (!hasSound)
                return;

            if (suddenSound && (DateTime.Now - _lastScaredTime).TotalMilliseconds > 900)
            {
                StartScareSequence(leftLevel, rightLevel, stereo);
                return;
            }

            // Громкость от 0 до 1
            double intensity = Clamp((totalLevel - 0.015) / 0.070, 0.0, 1.0);

            double lookX;
            double lookY;

            if (stereo)
            {
                double diff = rightLevel - leftLevel;
                double direction = Clamp(diff / 0.040, -1.0, 1.0);

                lookX = direction * MaxLookX;
                lookY = -MaxLookY * intensity * 0.20;

                // Чем громче звук, тем БОЛЬШЕ глаза
                double leftScaleX = 1.00 + 0.28 * intensity;
                double leftScaleY = 1.00 + 0.40 * intensity;
                double rightScaleX = 1.00 + 0.28 * intensity;
                double rightScaleY = 1.00 + 0.40 * intensity;

                double leftAngle = 0;
                double rightAngle = 0;

                // Сохраняем живость/наклон при направлении звука
                if (direction < -0.15)
                {
                    leftAngle = -8;
                    rightAngle = 4;
                }
                else if (direction > 0.15)
                {
                    leftAngle = -4;
                    rightAngle = 8;
                }

                ApplyFace(
                    lookX, lookY,
                    leftScaleX, leftScaleY, leftAngle,
                    rightScaleX, rightScaleY, rightAngle,
                    110);
            }
            else
            {
                lookX = 0;
                lookY = -MaxLookY * intensity * 0.28;

                double scaleX = 1.00 + 0.18 * intensity;
                double scaleY = 1.00 + 0.26 * intensity;

                ApplyFace(
                    lookX, lookY,
                    scaleX, scaleY, -2,
                    scaleX, scaleY, 2,
                    110);
            }
        }

        private void StartScareSequence(double leftLevel, double rightLevel, bool stereo)
        {
            _isInScareSequence = true;
            _lastScaredTime = DateTime.Now;

            double scareX = 0;
            double leftAngle = -8;
            double rightAngle = 8;

            if (stereo)
            {
                double diff = rightLevel - leftLevel;
                double direction = Clamp(diff / 0.035, -1.0, 1.0);
                scareX = direction * 175;

                if (direction < -0.15)
                {
                    leftAngle = -16;
                    rightAngle = 10;
                }
                else if (direction > 0.15)
                {
                    leftAngle = -10;
                    rightAngle = 16;
                }
            }

            ApplyFace(
    scareX, -24,
    1.20, 1.36, leftAngle,
    1.28, 1.30, rightAngle,
    90);

            // Коротко держим испуг
            var settleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(260)
            };

            settleTimer.Tick += (_, __) =>
            {
                settleTimer.Stop();

                // Начало спокойного длинного возврата
                ApplyFace(
                    0, -6,
                    1.08, 1.16, -2,
                    1.08, 1.16, 2,
                    900);

                _calmUntil = DateTime.Now.AddSeconds(2.6);

                var endScareTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(950)
                };

                endScareTimer.Tick += (_, __2) =>
                {
                    endScareTimer.Stop();
                    _isInScareSequence = false;
                };

                endScareTimer.Start();
            };

            settleTimer.Start();
        }

        private void SettleCenter()
        {
            ApplyFace(
                0, 0,
                1.00, 1.00, 0,
                1.00, 1.00, 0,
                340);
        }

        private void CalmBreathing()
        {
            ApplyFace(
                0, 0,
                0.985, 1.025, -1.5,
                0.985, 1.025, 1.5,
                520);
        }

        private void LookIdleLeft()
        {
            ApplyFace(
                -92, 0,
                0.89, 1.12, -9,
                0.97, 1.05, 5,
                320);
        }

        private void LookIdleRight()
        {
            ApplyFace(
                92, 0,
                0.97, 1.05, -5,
                0.89, 1.12, 9,
                320);
        }

        private void LookIdleLeftUp()
        {
            ApplyFace(
                -74, -12,
                0.88, 1.14, -10,
                0.98, 1.04, 6,
                340);
        }

        private void LookIdleRightUp()
        {
            ApplyFace(
                74, -12,
                0.98, 1.04, -6,
                0.88, 1.14, 10,
                340);
        }

        private void SetNeutral(bool immediate = false)
        {
            int duration = immediate ? 1 : 220;
            ApplyFace(0, 0, 1.0, 1.0, 0, 1.0, 1.0, 0, duration);
        }

        private void ApplyFace(
            double lookX,
            double lookY,
            double leftScaleX,
            double leftScaleY,
            double leftAngle,
            double rightScaleX,
            double rightScaleY,
            double rightAngle,
            int durationMs)
        {
            AnimateDouble(LeftEyeTranslate, TranslateTransform.XProperty, lookX, durationMs);
            AnimateDouble(LeftEyeTranslate, TranslateTransform.YProperty, lookY, durationMs);
            AnimateDouble(RightEyeTranslate, TranslateTransform.XProperty, lookX, durationMs);
            AnimateDouble(RightEyeTranslate, TranslateTransform.YProperty, lookY, durationMs);

            AnimateDouble(LeftEyeScale, ScaleTransform.ScaleXProperty, leftScaleX, durationMs);
            AnimateDouble(LeftEyeScale, ScaleTransform.ScaleYProperty, leftScaleY, durationMs);
            AnimateDouble(RightEyeScale, ScaleTransform.ScaleXProperty, rightScaleX, durationMs);
            AnimateDouble(RightEyeScale, ScaleTransform.ScaleYProperty, rightScaleY, durationMs);

            AnimateDouble(LeftEyeRotate, RotateTransform.AngleProperty, leftAngle, durationMs);
            AnimateDouble(RightEyeRotate, RotateTransform.AngleProperty, rightAngle, durationMs);

            LeftEye.CornerRadius = new CornerRadius(BaseHeight * 0.28);
            RightEye.CornerRadius = new CornerRadius(BaseHeight * 0.28);
        }

        private void AnimateDouble(DependencyObject target, DependencyProperty property, double to, int durationMs)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            if (target is IAnimatable animatable)
            {
                animatable.BeginAnimation(property, animation);
            }
        }

        private static double Lerp(double from, double to, double t)
        {
            return from + (to - from) * t;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _idleLifeTimer.Stop();

                if (_waveIn != null)
                {
                    _waveIn.DataAvailable -= WaveIn_DataAvailable;
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
            }
            catch
            {
            }

            base.OnClosed(e);
        }
    }
}