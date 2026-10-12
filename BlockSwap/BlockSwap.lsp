;;; BlockSwap.lsp
;;; For batch runs with AutoScript (or any .scr batch tool).
;;; In each drawing:  1) delete every reference of the listed blocks
;;;                   2) purge (so the old block definitions are gone)
;;;                   3) insert the new blocks from their .dwg files
;;;
;;; Command: BLOCKSWAP

(vl-load-com)

;;; ===================== CONFIG - EDIT THIS =====================

;; Blocks to delete. Not case-sensitive. Wildcards work (* ? #).
;; Dynamic blocks are matched by their real name, not *U123.
(setq *BS:DeleteBlocks*
  '(
    "OLD_TITLEBLOCK"
    "OLD_LOGO"
    "REV_TAG*"
  )
)

;; Blocks to insert into Model Space after the purge.
;; Each line: ("path to block .dwg"  (X Y Z)  scale  rotation-in-degrees)
;; The block gets the file's name (NEW_LOGO.dwg -> block NEW_LOGO).
;; Use forward slashes in paths.
(setq *BS:NewBlocks*
  '(
    ("C:/CAD/Blocks/NEW_TITLEBLOCK.dwg" (0.0 0.0 0.0)   1.0 0.0)
    ("C:/CAD/Blocks/NEW_LOGO.dwg"       (30.0 10.0 0.0) 1.0 0.0)
  )
)

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

(defun BS:Match (name)
  (vl-some '(lambda (pat) (wcmatch (strcase name) (strcase pat))) *BS:DeleteBlocks*)
)

(defun BS:EffName (obj)
  (if (vlax-property-available-p obj 'EffectiveName)
    (vla-get-EffectiveName obj)
    (vla-get-Name obj)
  )
)

;; Deletes matching block references in model space and every layout.
;; Locked layers are unlocked for the delete and re-locked afterwards.
(defun BS:DeleteBlocks (doc / locked ss i obj n)
  (setq locked '() n 0)
  (vlax-for lay (vla-get-Layers doc)
    (if (= (vla-get-Lock lay) :vlax-true)
      (progn (setq locked (cons lay locked)) (vla-put-Lock lay :vlax-false))
    )
  )
  (if (setq ss (ssget "_X" '((0 . "INSERT"))))
    (repeat (setq i (sslength ss))
      (setq obj (vlax-ename->vla-object (ssname ss (setq i (1- i)))))
      (if (and (BS:Match (BS:EffName obj))
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
(defun BS:CheckLeftovers (doc / left)
  (setq left "")
  (vlax-for blk (vla-get-Blocks doc)
    (if (and (= (vla-get-IsLayout blk) :vlax-false)
             (= (vla-get-IsXRef blk) :vlax-false)
             (BS:Match (vla-get-Name blk))
        )
      (setq left (strcat left " " (vla-get-Name blk)))
    )
  )
  (if (/= left "")
    (BS:Log (strcat "WARNING still defined after purge (nested?):" left))
  )
)

(defun BS:InsertBlocks (doc / ms n res)
  (setq ms (vla-get-ModelSpace doc) n 0)
  (foreach item *BS:NewBlocks*
    (if (findfile (car item))
      (if (vl-catch-all-error-p
            (setq res (vl-catch-all-apply 'vla-InsertBlock
                        (list ms
                              (vlax-3d-point (cadr item))
                              (car item)
                              (caddr item) (caddr item) (caddr item)
                              (* pi (/ (cadddr item) 180.0))
                        )
                      )
            )
          )
        (BS:Log (strcat "ERROR inserting " (car item) ": " (vl-catch-all-error-message res)))
        (setq n (1+ n))
      )
      (BS:Log (strcat "MISSING FILE " (car item)))
    )
  )
  n
)

(defun c:BLOCKSWAP (/ doc del ins)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (vla-StartUndoMark doc)
  (setq del (BS:DeleteBlocks doc))
  (BS:Purge doc)
  (BS:CheckLeftovers doc)
  (setq ins (BS:InsertBlocks doc))
  (vla-EndUndoMark doc)
  (BS:Log (strcat "deleted " (itoa del) " block(s), inserted " (itoa ins) " block(s)"))
  (princ)
)

(princ "\nBlockSwap loaded. Run BLOCKSWAP.")
(princ)
