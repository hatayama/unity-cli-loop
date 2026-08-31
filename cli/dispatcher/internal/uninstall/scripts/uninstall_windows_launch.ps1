$EncodedDeletion = '{{ENCODED_DELETION}}'
Start-Process -FilePath 'powershell' -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$EncodedDeletion) -WindowStyle Hidden
