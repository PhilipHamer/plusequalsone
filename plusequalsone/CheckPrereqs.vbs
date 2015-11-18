' TODO make a proper installer and get rid of this script...
Const checkUni = 10004
Const xUni = 10008
Dim objShell, regVal, evtVal, vc2013, wowneeded, vc2013wow, dotnet4, report, reportEvtSrc
Set objShell = WScript.CreateObject("WScript.Shell")

' detect 64/32 bit
wowneeded = objShell.Environment("Process")("PROCESSOR_ARCHITECTURE") <> "x86" or objShell.Environment("Process")("PROCESSOR_ARCHITEW6432") <> ""

On Error Resume Next

' vc2013 redist?
regVal = objShell.RegRead("HKLM\Software\Microsoft\DevDiv\VC\Servicing\12.0\RuntimeMinimum\Install")
vc2013 = err.number = 0 and regVal = 1
err.clear

' vc2013 redist both x64 and x86 required on 64 bit
if wowneeded then
	regVal = objShell.RegRead("HKLM\Software\Wow6432Node\Microsoft\DevDiv\VC\Servicing\12.0\RuntimeMinimum\Install")
	vc2013wow = err.number = 0 and regVal = 1
	err.clear
end if

' .net 4?
regVal = objShell.RegRead("HKLM\Software\Microsoft\NET Framework Setup\NDP\v4.0\Client\Install")
if err.number = 0 and regVal = 1 then
	err.clear
	dotnet4 = True
else
	err.clear
	regVal = objShell.RegRead("HKLM\Software\Microsoft\NET Framework Setup\NDP\v4\Client\Install")
	if err.number = 0 and regVal = 1 then
		err.clear
		dotnet4 = True
	else
		err.clear
		regVal = objShell.RegRead("HKLM\Software\Microsoft\NET Framework Setup\NDP\v4.0\Full\Install")
		if err.number = 0 and regVal = 1 then
			dotnet4 = True
		else
			dotnet4 = False
		end if
		err.clear
	end if
end if
		
' create event source
evtVal = objShell.Run("eventcreate /ID 1 /L Application /T Information /SO Notepad+=1 /D ""Event source created successfully""", 0, True)
if evtVal <> 0 then
	reportEvtSrc = vbCrLf & vbCrlf & "Failed to create event source. " & ChrW(xUni) & vbCrLf & "Please try running as Administrator."
else
    reportEvtSrc = ""
end if
		
' report
report = "Visual C++ 2013 runtime ("
if wowneeded then
	report = report & "x64) "
else
	report = report & "x86) "
end if
if vc2013 then
	report = report & ChrW(checkUni)
else
	report = report & ChrW(xUni)
end if
		
if wowneeded then
	report = report & vbCrLf & "Visual C++ 2013 runtime (x86) "
	if vc2013wow then
		report = report & ChrW(checkUni)
	else
		report = report & ChrW(xUni)
	end if
end if
		
report = report & vbCrLf & ".NET Framework 4.0 "
if dotnet4 then
	report = report & ChrW(checkUni)
else
	report = report & ChrW(xUni)
end if

Wscript.echo report & reportEvtSrc
		
if not vc2013 or (wowneeded and not vc2013wow) then
	objShell.Run "http://www.microsoft.com/en-us/download/details.aspx?id=40784"
end if
if not dotnet4 then
	objShell.Run "https://www.microsoft.com/en-us/download/details.aspx?id=17851"
end if
