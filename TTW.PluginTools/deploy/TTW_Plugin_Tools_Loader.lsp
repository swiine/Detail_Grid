;===============================LOADUP MESSAGE================================
;   TTW Plugin Tools loader.
;   Loads the TTW Plugin Tools launcher (the window: TTWPLUGINS / TTWPLUGINRUN)
;   once per session. Can live anywhere - it loads the launcher from the path below.
;   Put a copy where StandardisedFileOpening picks up lisps (Lisp_Routines) so it
;   loads at startup, or add it to APPLOAD > Startup Suite.
;
;   The launcher is copied from A: to the user's PC and loaded from there. That
;   keeps the copy on A: unlocked, so it can be updated while people are working;
;   everyone gets the update the next time they start Civil 3D.
;   (The plugin tools themselves are shadow-copied the same way by the launcher.)
(princ (strcat "\n|-|  " "Loading TTW Plugin Tools Loader..."))
;=============================================================================

;==================================MANUAL VARIABLES===========================
;   Folder holding TTW.PluginTools.dll, its config and System.Reflection.MetadataLoadContext.dll.
;   It sits in the plugins folder alongside the plugins it runs, in its own folder.
;   To release an update, replace the files in this folder.
(setq *path_TTWPluginToolsSource*
	"A:\\Civil\\AutoCAD\\Global\\Australia\\NSW\\_default\\Scripts\\_under_development\\Plugins\\TTW.PluginTools")

;   Only load into this release. The launcher is built for AutoCAD / Civil 3D 2026 (.NET 8).
(setq *var_TTWPluginToolsAcadVer* "25.1*")
;=============================================================================


;   Prints to the command line and, when loaded by StandardisedFileOpening, to ShowLispLog
(defun ttwplugintools:log (pass msg)
	(setq msg (strcat (if pass "\n|+|  " "\n|!|  ") msg))
	(if lisp_LogAndPRINC (lisp_LogAndPRINC msg) (princ msg))
)

;   Creates a folder and any missing parents
(defun ttwplugintools:mkdirs (path / parent)
	(if (not (vl-file-directory-p path))
		(progn
			(setq parent (vl-filename-directory path))
			(if (and parent (/= (strcase parent) (strcase path))) (ttwplugintools:mkdirs parent))
			(vl-mkdir path)
		)
	)
	(vl-file-directory-p path)
)

;   A stamp from a file's modified date: "20261012T083015"
(defun ttwplugintools:stamp (file / st)
	(if (setq st (vl-file-systime file))
		(apply 'strcat
			(mapcar '(lambda (n w / s) (setq s (itoa n)) (while (< (strlen s) w) (setq s (strcat "0" s))) s)
				(list (nth 0 st) (nth 1 st) (nth 3 st) (nth 4 st) (nth 5 st) (nth 6 st))
				'(4 2 2 2 2 2)))
		"0"
	)
)

;   "a;b;c" -> ("a" "b" "c")
(defun ttwplugintools:split (s / p)
	(if (setq p (vl-string-search ";" s))
		(cons (substr s 1 p) (ttwplugintools:split (substr s (+ p 2))))
		(list s)
	)
)

;   Adds "folder\..." (the folder and everything under it) to TRUSTEDPATHS if missing
(defun ttwplugintools:trust (folder / tp)
	(setq tp (getvar "TRUSTEDPATHS"))
	(if (not (vl-some '(lambda (x) (wcmatch (strcase x) (strcat (strcase folder) "*")))
	                  (ttwplugintools:split tp)))
		(setvar "TRUSTEDPATHS" (strcat (if (= tp "") "" (strcat tp ";")) folder "\\..."))
	)
)

;   Copies every file in src (not sub-folders) into dst
(defun ttwplugintools:copyfiles (src dst / ok)
	(setq ok T)
	(foreach f (vl-directory-files src nil 1)
		(if (not (vl-file-copy (strcat src "\\" f) (strcat dst "\\" f)))
			(setq ok nil))
	)
	ok
)

;   Deletes older local releases. Files still open in another Civil 3D are skipped.
(defun ttwplugintools:tidy (root keep)
	(foreach d (vl-remove-if '(lambda (x) (member x (list "." ".." keep))) (vl-directory-files root nil -1))
		(foreach f (vl-directory-files (strcat root "\\" d) nil 1)
			(vl-file-delete (strcat root "\\" d "\\" f)))
	)
)

(defun ttwplugintools:load ( / srcDll srcCfg root stamp local localDll result)
	(cond
		;Already loaded in this session (the blackboard is shared by all drawings)
		((vl-bb-ref '*ttwplugintools-loaded*) nil)

		((not (wcmatch (getvar "ACADVER") *var_TTWPluginToolsAcadVer*))
			(ttwplugintools:log nil "TTW Plugin Tools skipped: built for AutoCAD / Civil 3D 2026 only."))

		((not (findfile (setq srcDll (strcat *path_TTWPluginToolsSource* "\\TTW.PluginTools.dll"))))
			(ttwplugintools:log nil (strcat "TTW Plugin Tools not found: " srcDll)))

		(t
			(setq srcCfg (strcat *path_TTWPluginToolsSource* "\\TTW.PluginTools.config.json")
			      root   (strcat (getenv "LOCALAPPDATA") "\\TTW\\PluginTools\\launcher")
			      ;One local folder per release, named after the files' dates, so a new
			      ;release never has to overwrite a DLL another Civil 3D still has open.
			      stamp  (strcat (ttwplugintools:stamp srcDll)
			                     (if (findfile srcCfg) (strcat "-" (ttwplugintools:stamp srcCfg)) ""))
			      local  (strcat root "\\" stamp)
			      localDll (strcat local "\\TTW.PluginTools.dll"))

			(if (and (not (findfile localDll)) (ttwplugintools:mkdirs local))
				(if (ttwplugintools:copyfiles *path_TTWPluginToolsSource* local)
					(ttwplugintools:tidy root stamp)
					;A half-finished copy is no use - clear it so the next start tries again
					(ttwplugintools:tidy root "")
				)
			)

			(if (findfile localDll)
				(ttwplugintools:trust root)
				;Couldn't copy (e.g. no local write access) - load straight from A: instead
				(setq localDll srcDll)
			)

			(setq result (vl-catch-all-apply 'command-s (list "_.NETLOAD" localDll)))
			;NETLOAD fails quietly, so ask the plugin itself whether it's there
			(if (and (not (vl-catch-all-error-p result))
			         (not (vl-catch-all-error-p (vl-catch-all-apply 'ttwplugins-loaded-p nil))))
				(progn
					(vl-bb-set '*ttwplugintools-loaded* T)
					(ttwplugintools:log T "TTW Plugin Tools loaded. Type TTWPLUGINS or use the ribbon button.")
				)
				(ttwplugintools:log nil (strcat "TTW Plugin Tools failed to load from " localDll
				                                " - see the NETLOAD message above."))
			)
		)
	)
	(princ)
)

(ttwplugintools:load)
(princ)
