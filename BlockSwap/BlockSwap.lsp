;;; BlockSwap.lsp
;;; For batch runs with AutoScript (or any .scr batch tool).
;;; In each drawing:  1) delete every reference of the blocks in DeleteList.txt
;;;                   2) purge (so the old block definitions are gone)
;;;                   3) copy the new blocks in from the template drawing,
;;;                      at the same position/scale/rotation they have there
;;;
;;; Command: BLOCKSWAP

(vl-load-com)

;;; ===================== CONFIG - EDIT THIS =====================

;; Text file with the block names to delete, one per line.
;; Not case-sensitive. Wildcards work (* ? #). Lines starting with ; are ignored.
(setq *BS:DeleteListFile* "C:/CAD/Scripts/DeleteList.txt")

;; Template drawing that holds the new blocks, placed where they should go.
(setq *BS:TemplateDwg* "C:/CAD/Scripts/Template.dwg")

;; Optional text file with the block names to copy from the template, one per line.
;; If the file doesn't exist or is empty, EVERY block in the template is copied.
(setq *BS:CopyListFile* "C:/CAD/Scripts/CopyList.txt")

;; Log file (one line per drawing). Set to nil to turn off.
(setq *BS:LogFile* "C:/CAD/Scripts/BlockSwap.log")

;;; ==============================================================

(defun BS:Log (msg / f)
  (princ (strcat "\nBlockSwap: " msg))
  (if (and *BS:LogFile* (setq f (open *BS:LogFile* "a")))
    (progn
      (write-line (strcat (getvar "DWGPREFIX") (getvar "DWGNAME") " | " msg) f)
      (close f)
    )
  )
)

(defun BS:Trim (s) (vl-string-trim " \t\r\n" s))

;; Reads non-empty, non-comment lines of a text file. Returns nil if missing.
(defun BS:ReadList (file / f line lst)
  (if (and file (findfile file) (setq f (open (findfile file) "r")))
    (progn
      (while (setq line (read-line f))
        (setq line (BS:Trim line))
        (if (and (/= line "") (/= (substr line 1 1) ";"))
          (setq lst (cons line lst))
        )
      )
      (close f)
      (reverse lst)
    )
  )
)

(defun BS:Match (name patterns)
  (vl-some '(lambda (pat) (wcmatch (strcase name) (strcase pat))) patterns)
)

(defun BS:EffName (obj)
  (if (vlax-property-available-p obj 'EffectiveName)
    (vla-get-EffectiveName obj)
    (vla-get-Name obj)
  )
)

;; Deletes matching block references in model space and every layout.
;; Locked layers are unlocked for the delete and re-locked afterwards.
(defun BS:DeleteBlocks (doc patterns / locked ss i obj n)
  (setq locked '() n 0)
  (vlax-for lay (vla-get-Layers doc)
    (if (= (vla-get-Lock lay) :vlax-true)
      (progn (setq locked (cons lay locked)) (vla-put-Lock lay :vlax-false))
    )
  )
  (if (setq ss (ssget "_X" '((0 . "INSERT"))))
    (repeat (setq i (sslength ss))
      (setq obj (vlax-ename->vla-object (ssname ss (setq i (1- i)))))
      (if (and (BS:Match (BS:EffName obj) patterns)
               (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-Delete (list obj))))
          )
        (setq n (1+ n))
      )
    )
  )
  (foreach lay locked (vla-put-Lock lay :vlax-true))
  n
)

;; Purge everything a few times to catch nested unused items.
(defun BS:Purge (doc)
  (repeat 3 (vla-PurgeAll doc))
)

;; Warn about any old block definitions that survived the purge
;; (usually because they are nested inside another block).
(defun BS:CheckLeftovers (doc patterns / left)
  (setq left "")
  (vlax-for blk (vla-get-Blocks doc)
    (if (and (= (vla-get-IsLayout blk) :vlax-false)
             (= (vla-get-IsXRef blk) :vlax-false)
             (BS:Match (vla-get-Name blk) patterns)
        )
      (setq left (strcat left " " (vla-get-Name blk)))
    )
  )
  (if (/= left "")
    (BS:Log (strcat "WARNING still defined after purge (nested?):" left))
  )
)

;; Opens the template in the background (no window) via ObjectDBX.
(defun BS:OpenTemplate (path / dbx res)
  (setq dbx (vla-GetInterfaceObject
              (vlax-get-acad-object)
              (strcat "ObjectDBX.AxDbDocument." (substr (getvar "ACADVER") 1 2))
            )
  )
  (if (vl-catch-all-error-p (setq res (vl-catch-all-apply 'vla-Open (list dbx path))))
    (progn (vlax-release-object dbx) nil)
    dbx
  )
)

;; Target layout with the same name as the template layout. If there isn't one
;; (paper space only), falls back to the drawing's first paper space layout.
(defun BS:TargetLayout (doc name / lay first)
  (setq lay (vl-catch-all-apply 'vla-Item (list (vla-get-Layouts doc) name)))
  (if (vl-catch-all-error-p lay)
    (progn
      (vlax-for l (vla-get-Layouts doc)
        (if (and (/= (strcase (vla-get-Name l)) "MODEL")
                 (or (not first) (< (vla-get-TabOrder l) (vla-get-TabOrder first)))
            )
          (setq first l)
        )
      )
      (if first (BS:Log (strcat "no layout \"" name "\", using \"" (vla-get-Name first) "\"")))
      first
    )
    lay
  )
)

;; Copies matching block references from every layout of the template
;; (Model included) into the same layout of this drawing.
(defun BS:CopyFromTemplate (doc / patterns dbx objs arr target n res)
  (setq patterns (BS:ReadList *BS:CopyListFile*) n 0)
  (if (not (findfile *BS:TemplateDwg*))
    (progn (BS:Log (strcat "MISSING TEMPLATE " *BS:TemplateDwg*)) 0)
    (if (not (setq dbx (BS:OpenTemplate (findfile *BS:TemplateDwg*))))
      (progn (BS:Log (strcat "COULD NOT OPEN TEMPLATE " *BS:TemplateDwg*)) 0)
      (progn
        (vlax-for tlay (vla-get-Layouts dbx)
          (setq objs '())
          (vlax-for ent (vla-get-Block tlay)
            (if (and (= (vla-get-ObjectName ent) "AcDbBlockReference")
                     (or (not patterns) (BS:Match (BS:EffName ent) patterns))
                )
              (setq objs (cons ent objs))
            )
          )
          (if objs
            (if (setq target (BS:TargetLayout doc (vla-get-Name tlay)))
              (progn
                (setq arr (vlax-make-safearray vlax-vbObject (cons 0 (1- (length objs)))))
                (vlax-safearray-fill arr objs)
                (if (vl-catch-all-error-p
                      (setq res (vl-catch-all-apply 'vla-CopyObjects
                                  (list dbx arr (vla-get-Block target))))
                    )
                  (BS:Log (strcat "ERROR copying into \"" (vla-get-Name target) "\": "
                                  (vl-catch-all-error-message res)))
                  (setq n (+ n (length objs)))
                )
              )
              (BS:Log (strcat "no layout to copy \"" (vla-get-Name tlay) "\" blocks into"))
            )
          )
        )
        (vlax-release-object dbx)
        n
      )
    )
  )
)

(defun c:BLOCKSWAP (/ doc patterns del ins)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (if (not (setq patterns (BS:ReadList *BS:DeleteListFile*)))
    (BS:Log (strcat "DELETE LIST MISSING OR EMPTY " *BS:DeleteListFile* " - nothing done"))
    (progn
      (vla-StartUndoMark doc)
      (setq del (BS:DeleteBlocks doc patterns))
      (BS:Purge doc)
      (BS:CheckLeftovers doc patterns)
      (setq ins (BS:CopyFromTemplate doc))
      (vla-EndUndoMark doc)
      (BS:Log (strcat "deleted " (itoa del) " block(s), copied in " (itoa ins) " block(s)"))
    )
  )
  (princ)
)

(princ "\nBlockSwap loaded. Run BLOCKSWAP.")
(princ)
