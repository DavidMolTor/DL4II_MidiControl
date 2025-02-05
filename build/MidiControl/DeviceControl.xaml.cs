﻿/*
MIDI CONTROL 2023

DeviceControl.cs

- Description: Device control panel for DL4
- Author: David Molina Toro
- Date: 01 - 02 - 2023
- Version: 1.7
*/

using System;
using System.Linq;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Collections.Generic;

//MIDI libraries
using Sanford.Multimedia.Midi;

namespace MidiControl
{
    public partial class DeviceControl : UserControl
    {
        /*
        Public constructor
        */
        public DeviceControl()
        {
            InitializeComponent();

            //Set the error handling call
            IControlMIDI.Instance.ErrorMessage  += HandleErrorMessage;

            //Set the alternative button event
            altButton.AltButtonPressed          += HandleAltButtonPressed;

            //Set the footswitch events
            footswitch_A.FootswitchPressed      += HandleFootswitchPressed;
            footswitch_A.FootswitchHold         += HandleFootswitchHold;
            footswitch_B.FootswitchPressed      += HandleFootswitchPressed;
            footswitch_B.FootswitchHold         += HandleFootswitchHold;
            footswitch_C.FootswitchPressed      += HandleFootswitchPressed;
            footswitch_C.FootswitchHold         += HandleFootswitchHold;

            //Set the tap and set footswitches
            footswitch_TAP.FootswitchPressed    += HandleFootswitchPressed;
            footswitch_SET.FootswitchPressed    += HandleFootswitchPressed;

            //Set the current preset configuration
            SetDevice(IControlConfig.Instance.GetPreset(IControlConfig.Instance.GetSelectedPreset()));

            //Initialize the device update timer
            System.Timers.Timer timerUpdateDevice   = new System.Timers.Timer(Constants.DEVICE_UPDATE_PERIOD);
            timerUpdateDevice.Elapsed               += TimerUpdateDevice_Elapsed;
            timerUpdateDevice.Enabled               = true;

            //Initialize the tempo blink task
            Task.Run(TempoBlinkTask);
        }

        //Current configuration structure
        bool bReload = false;
        DeviceConfig configDevice;

        //Current note subdivision variable
        int iCurrentSubdivision = 0;

        //Tap footswitch control objects
        bool bTapMode               = false;
        object lockTapMode          = new object();
        AutoResetEvent eventTapMode = new AutoResetEvent(false);

        /*
        Sets the given configuration
        */
        private void SetDevice(DeviceConfig config, bool bReset = false)
        {
            //Set the reaload flag
            bReload = true;

            //Remove all commands if needed
            if (bReset)
            {
                IControlMIDI.Instance.RemoveCommands();
            }

            //Reset the current configuration
            configDevice = new DeviceConfig()
            {
                iDelaySelected  = -1,
                iDelayTime      = -1,
                iDelayNotes     = -1,
                iDelayRepeats   = -1,
                iDelayTweak     = -1,
                iDelayTweez     = -1,
                iDelayMix       = -1,
                iReverbSelected = -1,
                iReverbDecay    = -1,
                iReverbTweak    = -1,
                iReverbRouting  = -1,
                iReverbMix      = -1
            };

            //Get the currently selected preset
            int iPreset     = IControlConfig.Instance.GetSelectedPreset();
            int iChannel    = IControlConfig.Instance.GetChannelMIDI();

            //Set the configurable elements
            textboxPreset.Text  = iPreset.ToString();
            textboxChannel.Text = iChannel.ToString();

            //Send the preset command if able
            IControlMIDI.Instance.AddCommand(ChannelCommand.ProgramChange, IControlConfig.Instance.GetChannelMIDI(), iPreset - 1);

            //Set the delay select control
            if (config.iDelaySelected < Constants.ALTDELAY_INITIAL)
            {
                knobDelaySelected.SetKnob(config.iDelaySelected, Constants.DICT_DELAY.Select(x => (int)x.Key).ToList(), false);
            }
            else
            {
                knobDelaySelected.SetKnob(config.iDelaySelected, Constants.DICT_LEGACY.Select(x => (int)x.Key).ToList(), false);
            }

            //Set all delay control knobs
            knobDelayTime.SetKnob(config.iDelayTime,        Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobDelayRepeats.SetKnob(config.iDelayRepeats,  Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobDelayTweak.SetKnob(config.iDelayTweak,      Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobDelayTweez.SetKnob(config.iDelayTweez,      Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobDelayMix.SetKnob(config.iDelayMix,          Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());

            //Set the reverb select knob steps
            knobReverbSelected.SetKnob(config.iReverbSelected, Constants.DICT_REVERB.Select(x => (int)x.Key).ToList(), false);

            //Set the reverb routing knob steps
            knobReverbRouting.SetKnob(config.iReverbRouting, Enum.GetValues(typeof(ReverRouting)).Cast<ReverRouting>().Select(x => (int)x).ToList(), true, true);

            //Set all delay control knobs
            knobReverbDecay.SetKnob(config.iReverbDecay,    Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobReverbTweak.SetKnob(config.iReverbTweak,    Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());
            knobReverbMix.SetKnob(config.iReverbMix,        Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());

            //Reset all footswitches
            footswitch_A.SetStatus(FootswitchStatus.Off);
            footswitch_B.SetStatus(FootswitchStatus.Off);
            footswitch_C.SetStatus(FootswitchStatus.Off);

            //Set the active footswitch
            switch (IControlConfig.Instance.GetSelectedPreset() % 3)
            {
                case 0:
                    footswitch_C.SetStatus(FootswitchStatus.Green);
                    break;
                case 1:
                    footswitch_A.SetStatus(FootswitchStatus.Green);
                    break;
                case 2:
                    footswitch_B.SetStatus(FootswitchStatus.Green);
                    break;
            }

            //Set the alternative button status
            if (config.iDelaySelected < Constants.ALTDELAY_INITIAL)
            {
                altButton.SetStatus(AltButtonStatus.White);
            }
            else
            {
                altButton.SetStatus(AltButtonStatus.Green);
            }

            //Set the notes subdivision variable
            iCurrentSubdivision = config.iDelayNotes;
        }

        /*
        Device update timer elapsed function
        */
        private void TimerUpdateDevice_Elapsed(object source, ElapsedEventArgs e)
        {
            //Store the current configuration
            DeviceConfig configPrevious = new DeviceConfig(configDevice);

            //Set the new configuration parameters
            DeviceConfig configCurrent = new DeviceConfig()
            {
                iDelaySelected  = knobDelaySelected.Status,
                iDelayTime      = knobDelayTime.Status,
                iDelayNotes     = iCurrentSubdivision,
                iDelayRepeats   = knobDelayRepeats.Status,
                iDelayTweak     = knobDelayTweak.Status,
                iDelayTweez     = knobDelayTweez.Status,
                iDelayMix       = knobDelayMix.Status,
                iReverbSelected = knobReverbSelected.Status,
                iReverbDecay    = knobReverbDecay.Status,
                iReverbTweak    = knobReverbTweak.Status,
                iReverbRouting  = knobReverbRouting.Status,
                iReverbMix      = knobReverbMix.Status
            };

            //Get the selected MIDI channel
            int iChannel = IControlConfig.Instance.GetChannelMIDI();

            //Send the selected delay command if able
            if (configCurrent.iDelaySelected != configPrevious.iDelaySelected)
            {
                if (configCurrent.iDelaySelected == (int)DelayModels.Looper)
                {
                    //Enable the classic looper mode
                    IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.LooperMode, 64);
                }
                else
                {
                    //Disable the classic looper mode
                    IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.LooperMode, 0);

                    //Set the selected delay model
                    IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelaySelected, configCurrent.iDelaySelected);
                }
            }

            lock (lockTapMode)
            {
                //Check the tap mode status
                if (bTapMode)
                {
                    bTapMode = Constants.DICT_SUBDIVISIONS.Values.Contains(configCurrent.iDelayTime);
                }

                //Send the delay notes command if able
                if (configCurrent.iDelayNotes != configPrevious.iDelayNotes && bTapMode)
                    IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayNotes, configCurrent.iDelayNotes);

                //Send the delay time command if able
                if (configCurrent.iDelayTime != configPrevious.iDelayTime && !bTapMode)
                    IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayTime, configCurrent.iDelayTime);
            }

            //Send the delay repeats command if able
            if (configCurrent.iDelayRepeats != configPrevious.iDelayRepeats)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayRepeats, configCurrent.iDelayRepeats);

            //Send the delay tweak command if able
            if (configCurrent.iDelayTweak != configPrevious.iDelayTweak)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayTweak, configCurrent.iDelayTweak);

            //Send the delay tweez command if able
            if (configCurrent.iDelayTweez != configPrevious.iDelayTweez)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayTweez, configCurrent.iDelayTweez);

            //Send the delay mix command if able
            if (configCurrent.iDelayMix != configPrevious.iDelayMix)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayMix, configCurrent.iDelayMix);

            //Send the reverb selected command if able
            if (configCurrent.iReverbSelected != configPrevious.iReverbSelected)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.ReverbSelected, configCurrent.iReverbSelected);

            //Send the reverb decay command if able
            if (configCurrent.iReverbDecay != configPrevious.iReverbDecay)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.ReverbDecay, configCurrent.iReverbDecay);

            //Send the reverb tweak command if able
            if (configCurrent.iReverbTweak != configPrevious.iReverbTweak)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.ReverbTweak, configCurrent.iReverbTweak);

            //Send the reverb routing command if able
            if (configCurrent.iReverbRouting != configPrevious.iReverbRouting)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.ReverbRouting, configCurrent.iReverbRouting);

            //Send the reverb mix command if able
            if (configCurrent.iReverbMix != configPrevious.iReverbMix)
                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.ReverbMix, configCurrent.iReverbMix);

            //Check the delay selector status and set the labels
            if (configCurrent.iDelaySelected < Constants.ALTDELAY_INITIAL)
            {
                string[] sParts = Constants.DICT_DELAY[(DelayModels)configCurrent.iDelaySelected].Split('|');
                Dispatcher.Invoke(new Action(() => labelDelayTweak.Content = sParts.First()));
                Dispatcher.Invoke(new Action(() => labelDelayTweez.Content = sParts.Last()));
            }
            else
            {
                string[] sParts = Constants.DICT_LEGACY[(LegacyModels)configCurrent.iDelaySelected].Split('|');
                Dispatcher.Invoke(new Action(() => labelDelayTweak.Content = sParts.First()));
                Dispatcher.Invoke(new Action(() => labelDelayTweez.Content = sParts.Last()));
            }

            //Set the reverb parameter labels
            Dispatcher.Invoke(new Action(() => labelReverbTweak.Content = Constants.DICT_REVERB[(ReverbModels)configCurrent.iReverbSelected]));
            switch ((ReverRouting)configCurrent.iReverbRouting)
            {
                case ReverRouting.ReverbDelay:
                    Dispatcher.Invoke(new Action(() => labelReverbRouting.Content = "REVERB ► DELAY"));
                    break;
                case ReverRouting.Parallel:
                    Dispatcher.Invoke(new Action(() => labelReverbRouting.Content = "REVERB  =  DELAY"));
                    break;
                case ReverRouting.DelayReverb:
                    Dispatcher.Invoke(new Action(() => labelReverbRouting.Content = "DELAY ► REVERB"));
                    break;
            }

            //Set the device configuration
            if (!bReload)
            {
                configDevice = new DeviceConfig(configCurrent);
            }
            else
            {
                bReload = false;
            }
        }

        /*
        Tempo blink task function
        */
        private void TempoBlinkTask()
        {
            while (true)
            {
                try
                {
                    //Get the tap mode value
                    bool bValue;
                    lock (lockTapMode)
                    {
                        bValue = bTapMode;
                    }

                    //Check the tap mode value
                    if (!bValue)
                    {
                        eventTapMode.WaitOne();
                    }

                    //Set the footswitch blink status
                    Dispatcher.Invoke(new Action(() => footswitch_TAP.SetStatus(FootswitchStatus.Red)));
                    Thread.Sleep(Constants.FOOTSWITCH_BLINK_PERIOD / 2);

                    //Reset the footswitch blink status
                    Dispatcher.Invoke(new Action(() => footswitch_TAP.SetStatus(FootswitchStatus.Off)));
                    Thread.Sleep(Constants.FOOTSWITCH_BLINK_PERIOD);
                }
                catch (KeyNotFoundException)
                {
                    continue;
                }
            }
        }

        /*
        Sets the given message as an error
        */
        private void HandleErrorMessage(string sMessage)
        {
            Dispatcher.Invoke(new Action(() => labelError.Content = sMessage));
        }

        /*
        Alternative button pressed handler function
        */
        private void HandleAltButtonPressed(object sender, EventArgs e)
        {
            //Set the delay select knob list
            if (altButton.Status == AltButtonStatus.White)
            {
                int iSelected = Constants.DICT_DELAY.Keys.ToList().IndexOf((DelayModels)configDevice.iDelaySelected);
                knobDelaySelected.SetKnob((int)Constants.DICT_LEGACY.ElementAt(iSelected).Key, Constants.DICT_LEGACY.Select(x => (int)x.Key).ToList(), false);
            }
            else
            {
                int iSelected = Constants.DICT_LEGACY.Keys.ToList().IndexOf((LegacyModels)configDevice.iDelaySelected);
                knobDelaySelected.SetKnob((int)Constants.DICT_DELAY.ElementAt(iSelected).Key, Constants.DICT_DELAY.Select(x => (int)x.Key).ToList(), false);
            }

            //Set the alternative button status
            altButton.SetStatus(altButton.Status == AltButtonStatus.White ? AltButtonStatus.Green : AltButtonStatus.White);
        }

        /*
        Footswitch pressed handler function
        */
        private void HandleFootswitchPressed(object sender, EventArgs e)
        {
            //Check if any footswitch is blinking
            if (!footswitch_A.Blinking && !footswitch_B.Blinking && !footswitch_C.Blinking)
            {
                //Get the selected MIDI channel
                int iChannel = IControlConfig.Instance.GetChannelMIDI();

                //Check the type of footswitch
                switch (((Footswitch)sender).Name.Split('_').Last())
                {
                    case "A":
                    case "B":
                    case "C":
                        //Check the pressed button
                        switch (((Footswitch)sender).Status)
                        {
                            case FootswitchStatus.Off:
                                //Check the footswitch pressed
                                if (sender == footswitch_A)
                                {
                                    //Set the current configuration and store the selected preset
                                    DeviceConfig config = IControlConfig.Instance.GetPreset(1);
                                    IControlConfig.Instance.SaveSelectedPreset(1);
                                    SetDevice(config, true);
                                }
                                else if (sender == footswitch_B)
                                {
                                    //Set the current configuration and store the selected preset
                                    DeviceConfig config = IControlConfig.Instance.GetPreset(2);
                                    IControlConfig.Instance.SaveSelectedPreset(2);
                                    SetDevice(config, true);
                                }
                                else if (sender == footswitch_C)
                                {
                                    //Set the current configuration and store the selected preset
                                    DeviceConfig config = IControlConfig.Instance.GetPreset(3);
                                    IControlConfig.Instance.SaveSelectedPreset(3);
                                    SetDevice(config, true);
                                }
                                break;
                            case FootswitchStatus.Green:
                                //Send the preset bypass command
                                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.PresetBypass, 0);

                                //Set the footswitch status
                                ((Footswitch)sender).SetStatus(FootswitchStatus.Dim);
                                break;
                            case FootswitchStatus.Dim:
                                //Send the preset bypass command
                                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.PresetBypass, 64);

                                //Set the footswitch status
                                ((Footswitch)sender).SetStatus(FootswitchStatus.Green);
                                break;
                        }
                        break;
                    case "TAP":
                        lock (lockTapMode)
                        {
                            //Get the next note subdivision setting
                            List<int> listSubdivisions = Enum.GetValues(typeof(TimeSubdivisions)).Cast<int>().ToList();
                            int iDelayNotes = listSubdivisions.IndexOf(configDevice.iDelayNotes) + (bTapMode ? 1 : 0);

                            //Set the note subdivision variable
                            iCurrentSubdivision = iDelayNotes < listSubdivisions.Count ? iDelayNotes : 0;

                            //Set the subdivisions knob
                            knobDelayTime.SetKnob(Constants.DICT_SUBDIVISIONS[(TimeSubdivisions)iCurrentSubdivision], Enumerable.Range(0, Constants.MAX_KNOB_VALUES).ToList());

                            //Check the tap mode value
                            if (!bTapMode)
                            {
                                //Send the delay notes command
                                IControlMIDI.Instance.AddCommand(ChannelCommand.Controller, iChannel, (int)SettingsCC.DelayNotes, iCurrentSubdivision);

                                //Set the tap mode value
                                bTapMode = true;

                                //Set the tap mode event
                                eventTapMode.Set();
                            }
                        }
                        break;
                    case "SET":
                        //Save the current preset
                        SaveCurrentPreset();
                        break;
                }
            }
        }

        /*
        Footswitch hold handler function
        */
        private void HandleFootswitchHold(object sender, EventArgs e)
        {
            //Save the current preset
            SaveCurrentPreset();
        }

        /*
        Saves the current device configuration
        */
        private void SaveCurrentPreset()
        {
            int iPreset = 0;

            //Get the current selected preset
            Dispatcher.Invoke(new Action(() => iPreset = int.Parse(textboxPreset.Text)));

            //Check the preset number
            if (iPreset >= Constants.PRESET_COUNT_MIN && iPreset <= Constants.PRESET_COUNT_MAX)
            {
                IControlConfig.Instance.SavePreset(iPreset, configDevice);
            }
        }

        /*
        Channel button clicked event handler
        */
        private void ButtonChannel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            //Get the current selected channel
            if (int.TryParse(textboxChannel.Text, out int iChannel))
            {
                //Get the type of button pressed and set the channel
                string sDirection = ((Button)sender).Name.Split('_').Last();
                switch (sDirection)
                {
                    case "Up":
                        iChannel++;
                        break;
                    case "Down":
                        iChannel--;
                        break;
                }

                //Check if the channel is outside bounds
                iChannel = iChannel > Constants.CHANNEL_COUNT_MAX ? Constants.CHANNEL_COUNT_MAX : iChannel;
                iChannel = iChannel < Constants.CHANNEL_COUNT_MIN ? Constants.CHANNEL_COUNT_MIN : iChannel;

                //Set the channel text
                textboxChannel.Text = iChannel.ToString();
            }
        }

        /*
        Channel text changed event handler
        */
        private void TextboxChannel_TextChanged(object sender, TextChangedEventArgs e)
        {
            //Check if the channel text is empty
            if (!string.IsNullOrEmpty(textboxChannel.Text))
            {
                //Get the current selected channel
                if (int.TryParse(textboxChannel.Text, out int iChannel))
                {
                    //Check if the channel is outside bounds
                    iChannel = iChannel > Constants.CHANNEL_COUNT_MAX ? Constants.CHANNEL_COUNT_MAX : iChannel;
                    iChannel = iChannel < Constants.CHANNEL_COUNT_MIN ? Constants.CHANNEL_COUNT_MIN : iChannel;

                    //Save the selected MIDI channel
                    IControlConfig.Instance.SaveChannelMIDI(iChannel);
                }
            }
        }

        /*
        Preset button clicked event handler
        */
        private void ButtonPreset_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            //Get the current selected preset
            if (int.TryParse(textboxPreset.Text, out int iPreset))
            {
                //Get the type of button pressed and set the preset
                string sDirection = ((Button)sender).Name.Split('_').Last();
                switch (sDirection)
                {
                    case "Up":
                        iPreset++;
                        break;
                    case "Down":
                        iPreset--;
                        break;
                }

                //Check if the preset is outside bounds
                iPreset = iPreset > Constants.PRESET_COUNT_MAX ? Constants.PRESET_COUNT_MAX : iPreset;
                iPreset = iPreset < Constants.PRESET_COUNT_MIN ? Constants.PRESET_COUNT_MIN : iPreset;

                //Set the preset text
                textboxPreset.Text = iPreset.ToString();
            }
        }

        /*
        Preset text changed event handler
        */
        private void TextboxPreset_TextChanged(object sender, TextChangedEventArgs e)
        {
            //Check if the preset text is empty
            if (!string.IsNullOrEmpty(textboxPreset.Text))
            {
                //Get the current selected preset
                if (int.TryParse(textboxPreset.Text, out int iPreset))
                {
                    //Check if the preset is outside bounds
                    iPreset = iPreset > Constants.PRESET_COUNT_MAX ? Constants.PRESET_COUNT_MAX : iPreset;
                    iPreset = iPreset < Constants.PRESET_COUNT_MIN ? Constants.PRESET_COUNT_MIN : iPreset;

                    //Save the selected preset
                    IControlConfig.Instance.SaveSelectedPreset(iPreset);

                    //Set the device configuration
                    DeviceConfig config = IControlConfig.Instance.GetPreset(iPreset);
                    SetDevice(config, true);
                }
            }
        }

        /*
        Check if the input character is a number
        */
        private void TextBoxNumber_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text.ToCharArray()[0]);
        }
    }
}
