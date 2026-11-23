# RayˣDesktop

![RayˣDesktop, with a blue and gold X against a galaxy](assets/RayXDesktop-banner.jpg)

**Maintained by D.Synx** · Based on [Ray Desktop by raydoomed](https://github.com/raydoomed/Ray-Desktop) · MIT

RayXDesktop displays a local Windows Child Session in a separate window. The toolbar lets you change the display mode and resolution, switch to fullscreen, transfer selected files, reconnect and close the session. It docks automatically when you open the session list. Use the dock button to keep it above the desktop or switch back to the floating toolbar.

[Guide en français](maintenance/README.fr.md) · [Stable 1.4.6](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.4.6) · [1.5.0n beta](https://github.com/D-Synx/Ray-Desktop/releases/tag/v1.5.0n)

## Aperçu de la barre · 1.4.6

**Fichiers** et **Reconnecter** sont accessibles directement dans la nouvelle barre.

![RayXDesktop 1.4.6 — nouvelle barre avec Affichage, Résolution, Fichiers et Reconnecter](assets/RayXDesktop-1.4.6.webp)

## Run

1. Download **RayXDesktop-Windows.zip** from the beta release Assets and extract it.
2. Keep both EXEs, both DLLs, and **RayXDesktop.bat** in the same folder.
3. Run **RayXDesktop.exe** for the current Windows account. To connect with another authorized account, set the account name in **RayXDesktop.bat**. RayXDesktop prompts for that account’s password when it connects.
4. If Child Sessions are disabled, an authorized administrator can run **EnableChildSessions.exe --enable-child-sessions**. A Windows sign-out and sign-in may be required after first activation.

The interface uses French on French Windows installations and English elsewhere. Pass **--lang=fr** or **--lang=en** to choose a language. Use **--dynamic** to start with a window-sized desktop. See the French guide for the existing account-based startup options.

## Sessions, files and graphics

- Open **More → Show sessions** to switch between the local desktop and authorized remote RDP connections. Add a connection by IP address, DNS name or direct .rdp file. Saved sessions return to the sidebar at startup. Click one to connect. RayXDesktop captures a thumbnail when the desktop opens and refreshes it when switching sessions. Select two sessions in succession, then choose **Side by side**. Windows supports one active local Child Session.
- **Reconnect** resets the selected RDP link and then retries. Use **Disconnect** to leave the desktop running subject to Windows policy. **Sign out of the local session** closes the applications in that Windows session.
- Copy and paste text in either direction with **Ctrl+C** and **Ctrl+V** in an embedded remote session, subject to Windows clipboard policies. Use the clipboard icon to toggle native RDP redirection for the selected session, then reconnect.
- Copy a file or folder in Explorer, then choose **Files → Send to RayXDesktop** or **Files → Retrieve from RayXDesktop**. Pick a destination folder. Standard Windows permissions and clipboard policy apply. A copy from the child session requires Windows to deliver the file selection to the main session.
- Use the two arrows beside **Files** to turn each direction of local RayXDesktop file transfer on or off independently. A filled indicator shows an enabled option. Native clipboard redirection follows the selected session’s clipboard setting and Windows policy.
- Choose **More → Prefer smooth display** to reduce visual effects and display resolution on the next local connection or new remote connection. **RDP graphics and codec** shows the effective GPU, H.264 hardware encoder and AVC 4:4:4 policies. Use its switches to change the local policy on the machine hosting the remote desktop, with confirmation and Windows administrator approval.

If an embedded desktop stays blank, select its card and read its connection status. Use **More → Refresh remote display** after Windows has signed in. **Open this address in Windows Remote Desktop** provides a comparison using the same account and a fresh Windows credentials prompt. **Copy remote connection diagnostics** captures the client disconnect code, the extended code and the sign-in stage.

After an early `0x00000107` disconnect with no specific cause, **Retry with certificate warning** appears in the affected view. After a successful sign-in in this mode, RayXDesktop remembers the setting per address and port. Windows still asks for credentials and handles any certificate warning. Uncheck **More → Ask me about certificate errors (next connection)** to forget the setting for the selected address.

`data.ini` next to `RayXDesktop.exe` stores the saved session addresses and account names, certificate warning modes, clipboard and display settings. Keep it when upgrading. Choose **Remove saved session** to remove a connection from the list.

Select a remote session and use **More → Save or update password** to save the password after a successful sign-in. You can also opt in when adding a connection. Windows DPAPI protects the password for the current user, normally on the same PC. RayXDesktop supplies it only in strict server-authentication mode. Choose **Forget saved password** to remove it.

**More → Reconnect all sessions at startup** is on by default. RayXDesktop connects saved sessions one at a time using usable stored credentials and strict server authentication. Sessions requiring credentials or a certificate decision stay available for manual sign-in. Turn off the global option to select any number of sessions individually with **Reconnect at startup**.

## Build and test

Use Windows PowerShell with the .NET Framework compiler and Windows SDK tools, including **AxImp.exe**. Run **.\tests\run-tests.ps1** for core tests and **.\maintenance\build-portable.ps1** for the portable package. The Windows workflow runs both the core and maintenance profile tests. Set **RAYDESKTOP_SDK_ROOT** if the SDK is installed outside the usual locations.

See the [French guide](maintenance/README.fr.md#vérifier-et-signer-le-zip) for checksum verification, optional Authenticode signing and the three-PC pilot checklist.

## Diagnose

Click the thin strip at the right edge or choose **More → Open activity log** to view recent entries. **Show activity log edge** controls whether the strip is visible when collapsed. The **More** menu can also open the log folder at **%LOCALAPPDATA%\RayDesktop\Logs**. Look for **RDP connected in** in `child-session.log` to distinguish the Windows connection wait from the time it takes the toolbar to appear.

## Credits

RayXDesktop maintenance and Windows packaging by **D.Synx**. Original Ray Desktop code by **raydoomed**. See [LICENSE](LICENSE).
