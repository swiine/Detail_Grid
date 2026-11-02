;;; ===========================================================================
;;; install_custom_ribbon.lsp
;;;
;;; Applies your custom ribbon (a partial CUIx) and your customization settings
;;; to the AutoCAD profile that is CURRENTLY ACTIVE (see the CPROFILE variable).
;;;
;;; Usage:
;;;   1. Edit the CONFIGURATION section below.
;;;   2. In AutoCAD, switch to the profile you want (OPTIONS > Profiles).
;;;   3. APPLOAD this file (or drag it into the drawing), then type INSTALLRIBBON.
;;;
;;; Safe to run more than once: paths are only added if missing, and the
;;; ribbon CUIx is reloaded so you always get the latest version.
;;; ===========================================================================

(vl-load-com)

;;; ===========================================================================
;;; CONFIGURATION - edit these values
;;; ===========================================================================

;; Full path to your partial customization file that holds the ribbon.
(setq *ICR-cuix-path* "C:\\CAD\\Custom\\MyRibbon.cuix")

;; Folders to add to "Support File Search Path" (OPTIONS > Files).
;; The folder holding the CUIx is added automatically.
(setq *ICR-support-paths*
  '("C:\\CAD\\Custom"
    "C:\\CAD\\Custom\\Lisp"
   )
)

;; Folders to add to "Trusted Locations" so your LISP/macros load without
;; security warnings. End a path with \\... to also trust its subfolders.
(setq *ICR-trusted-paths*
  '("C:\\CAD\\Custom\\..."
   )
)

;; Optional: other OPTIONS > Files paths. Use nil to leave a setting alone.
(setq *ICR-template-path*  nil) ; e.g. "C:\\CAD\\Templates"
(setq *ICR-qnew-template*  nil) ; e.g. "C:\\CAD\\Templates\\Company.dwt"
(setq *ICR-plotter-path*   nil) ; e.g. "C:\\CAD\\Plotters"
(setq *ICR-plotstyle-path* nil) ; e.g. "C:\\CAD\\Plot Styles"

;; Optional: workspace to switch to after loading (must already exist in
;; your main or partial CUIx). Use nil to keep the current workspace.
(setq *ICR-workspace* nil) ; e.g. "My Workspace"

;; System variables to set. Variables saved to the registry are stored in the
;; current profile; others are saved in the drawing or are session-only.
;; Delete or add lines to match your preferences.
(setq *ICR-sysvars*
  '(("FILEDIA"     1)   ; show file dialogs
    ("PICKFIRST"   1)   ; noun/verb selection
    ("PICKADD"     2)   ; shift to remove from selection
    ("CURSORSIZE"  100) ; full-screen crosshairs
    ("SECURELOAD"  1)   ; only load from trusted locations
   )
)

;;; ===========================================================================
;;; IMPLEMENTATION - no need to edit below this line
;;; ===========================================================================

(defun ICR:msg (s) (princ (strcat "\n  " s)))

;; Split "a;b;c" into ("a" "b" "c"), dropping empty entries.
(defun ICR:split (str / pos out)
  (while (setq pos (vl-string-search ";" str))
    (if (> pos 0) (setq out (cons (substr str 1 pos) out)))
    (setq str (substr str (+ pos 2)))
  )
  (if (/= str "") (setq out (cons str out)))
  (reverse out)
)

;; Normalise a path for comparison: upper case, no trailing backslash.
(defun ICR:norm (p)
  (setq p (strcase (vl-string-trim " " p)))
  (vl-string-right-trim "\\/" p)
)

;; Append NEW paths to the ";"-separated string EXISTING if not already there.
;; Returns (newString . addedCount).
(defun ICR:merge-paths (existing new / lst normed added)
  (setq lst    (ICR:split (if existing existing ""))
        normed (mapcar 'ICR:norm lst)
        added  0)
  (foreach p new
    (if (not (member (ICR:norm p) normed))
      (setq lst    (append lst (list p))
            normed (append normed (list (ICR:norm p)))
            added  (1+ added))
    )
  )
  (cons
    (if lst
      (apply 'strcat (cons (car lst) (mapcar '(lambda (x) (strcat ";" x)) (cdr lst))))
      ""
    )
    added
  )
)

;; Run FN with ARGS; print LABEL on failure instead of aborting.
(defun ICR:try (label fn args / res)
  (setq res (vl-catch-all-apply fn args))
  (if (vl-catch-all-error-p res)
    (progn (ICR:msg (strcat "! " label ": " (vl-catch-all-error-message res))) nil)
    (if res res T)
  )
)

(defun ICR:set-support-paths (files / dirs res)
  (setq dirs *ICR-support-paths*)
  (if *ICR-cuix-path*
    (setq dirs (cons (vl-filename-directory *ICR-cuix-path*) dirs))
  )
  (setq res (ICR:merge-paths (vla-get-SupportPath files) dirs))
  (if (> (cdr res) 0)
    (if (ICR:try "Support paths" 'vla-put-SupportPath (list files (car res)))
      (ICR:msg (strcat "Support paths: added " (itoa (cdr res)) " folder(s)."))
    )
    (ICR:msg "Support paths: already up to date.")
  )
)

(defun ICR:set-trusted-paths (/ dirs res)
  (setq dirs *ICR-trusted-paths*)
  (if *ICR-cuix-path*
    (setq dirs (cons (vl-filename-directory *ICR-cuix-path*) dirs))
  )
  (if (getvar "TRUSTEDPATHS")
    (progn
      (setq res (ICR:merge-paths (getvar "TRUSTEDPATHS") dirs))
      (if (> (cdr res) 0)
        (if (ICR:try "Trusted paths" 'setvar (list "TRUSTEDPATHS" (car res)))
          (ICR:msg (strcat "Trusted locations: added " (itoa (cdr res)) " folder(s)."))
        )
        (ICR:msg "Trusted locations: already up to date.")
      )
    )
    (ICR:msg "Trusted locations: not supported in this version, skipped.")
  )
)

(defun ICR:set-file-options (files)
  (if *ICR-template-path*
    (if (ICR:try "Template path" 'vla-put-TemplateDwgPath (list files *ICR-template-path*))
      (ICR:msg "Template folder set."))
  )
  (if *ICR-qnew-template*
    (if (ICR:try "QNEW template" 'vla-put-QNewTemplateFile (list files *ICR-qnew-template*))
      (ICR:msg "QNEW default template set."))
  )
  (if *ICR-plotter-path*
    (if (ICR:try "Plotter path" 'vla-put-PrinterConfigPath (list files *ICR-plotter-path*))
      (ICR:msg "Plotter config folder set."))
  )
  (if *ICR-plotstyle-path*
    (if (ICR:try "Plot style path" 'vla-put-PrinterStyleSheetPath (list files *ICR-plotstyle-path*))
      (ICR:msg "Plot style folder set."))
  )
)

(defun ICR:find-menugroup (groups file / found)
  (vlax-for g groups
    (if (and (not found)
             (= (strcase (vl-filename-base (vla-get-MenuFileName g)))
                (strcase (vl-filename-base file))))
      (setq found g)
    )
  )
  found
)

(defun ICR:load-ribbon (acad / groups existing)
  (cond
    ((not *ICR-cuix-path*)
     (ICR:msg "Ribbon: no CUIx configured, skipped."))
    ((not (findfile *ICR-cuix-path*))
     (ICR:msg (strcat "! Ribbon: file not found: " *ICR-cuix-path*)))
    (T
     (setq groups (vla-get-MenuGroups acad))
     ;; Unload first so an updated CUIx is picked up cleanly.
     (if (setq existing (ICR:find-menugroup groups *ICR-cuix-path*))
       (ICR:try "Unload old ribbon" 'vla-Unload (list existing))
     )
     (if (ICR:try "Load ribbon" 'vla-Load (list groups *ICR-cuix-path*))
       (ICR:msg (strcat "Ribbon: loaded " (vl-filename-base *ICR-cuix-path*)
                        (if existing " (reloaded)." ".")))
     )
    )
  )
)

(defun ICR:set-sysvars (/ n)
  (setq n 0)
  (foreach sv *ICR-sysvars*
    (if (getvar (car sv))
      (if (ICR:try (car sv) 'setvar (list (car sv) (cadr sv)))
        (setq n (1+ n)))
      (ICR:msg (strcat "! " (car sv) ": unknown variable, skipped."))
    )
  )
  (ICR:msg (strcat "System variables: set " (itoa n) " of "
                   (itoa (length *ICR-sysvars*)) "."))
)

(defun C:INSTALLRIBBON (/ acad prefs files cmdecho)
  (setq acad    (vlax-get-acad-object)
        prefs   (vla-get-Preferences acad)
        files   (vla-get-Files prefs)
        cmdecho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)

  (princ (strcat "\nInstalling custom ribbon into profile: \""
                 (getvar "CPROFILE") "\""))

  ;; Paths first, so the CUIx and anything it references can be found.
  (ICR:set-support-paths files)
  (ICR:set-trusted-paths)
  (ICR:set-file-options files)
  (ICR:load-ribbon acad)
  (ICR:set-sysvars)

  (if *ICR-workspace*
    (if (ICR:try "Workspace" 'setvar (list "WSCURRENT" *ICR-workspace*))
      (ICR:msg (strcat "Workspace: switched to " *ICR-workspace* ".")))
  )

  ;; Make sure the ribbon palette is visible.
  (vl-catch-all-apply 'vl-cmdf (list "_.RIBBON"))

  (setvar "CMDECHO" cmdecho)
  (princ (strcat "\nDone. Settings are saved to profile \"" (getvar "CPROFILE")
                 "\" when AutoCAD closes."))
  (princ)
)

(princ "\ninstall_custom_ribbon.lsp loaded. Type INSTALLRIBBON to apply it to the current profile.")
(princ)
